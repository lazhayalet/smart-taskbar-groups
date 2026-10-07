using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Windows.Dpi;
using TaskbarGroups.Windows.Interop;

namespace TaskbarGroups.Windows.Monitor
{
    /// <summary>
    /// Enumerates the real monitors and answers placement questions about them.
    /// </summary>
    /// <remarks>
    /// Replaces <c>frmMain.SetLocation()</c> and <c>frmMain.FindDockedTaskBars()</c>.
    /// Two behaviours changed deliberately:
    /// <list type="bullet">
    /// <item>A monitor and its taskbar rectangle are paired by <c>MONITORINFO</c>,
    /// which the shell fills in for one monitor at a time. The legacy code paired
    /// them by enumeration index and skipped screens without a dock, which is what
    /// produced <c>ArgumentOutOfRangeException</c>.</item>
    /// <item>Nothing is keyed on an index. A monitor is identified by its device
    /// name, so a secondary display that is temporarily disabled cannot make
    /// anything index out of range.</item>
    /// </list>
    /// </remarks>
    public sealed class MonitorService : IMonitorService
    {
        private readonly object _gate = new object();
        private MonitorQueryResult? _cached;
        private DateTime _cachedAtUtc = DateTime.MinValue;

        /// <summary>How long a monitor enumeration is reused before it is refreshed.</summary>
        public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(2);

        public MonitorQueryResult GetMonitors()
        {
            lock (_gate)
            {
                if (_cached != null && DateTime.UtcNow - _cachedAtUtc < CacheDuration)
                    return _cached;

                MonitorQueryResult fresh = Enumerate();
                _cached = fresh;
                _cachedAtUtc = DateTime.UtcNow;
                return fresh;
            }
        }

        /// <summary>Forces the next call to re-enumerate. Call after a display change.</summary>
        public void Invalidate()
        {
            lock (_gate)
            {
                _cached = null;
                _cachedAtUtc = DateTime.MinValue;
            }
        }

        public MonitorInfo? GetMonitorAt(Point physicalPoint) => GetMonitors().ByPoint(physicalPoint);

        public MonitorInfo? GetMonitorByDeviceName(string? deviceName) => GetMonitors().ByDeviceName(deviceName);

        public MonitorInfo? GetPrimary() => GetMonitors().Primary;

        public PopupPlacement CalculatePopupPlacement(PopupPlacementRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            MonitorQueryResult monitors = GetMonitors();
            if (request.AnchorOnTaskbar)
                request.AnchorOnTaskbar = IsOnDockedTaskbar(request.AnchorPoint);

            return PopupPlacementCalculator.Calculate(request, monitors, request.AnchorPoint);
        }

        /// <summary>Physical cursor position, or the origin when it cannot be read.</summary>
        public static Point GetCursorPosition()
        {
            try
            {
                if (NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
                    return new Point(pt.X, pt.Y);
            }
            catch (Exception)
            {
            }
            return Point.Empty;
        }

        /// <summary>
        /// True when the point lies on a docked taskbar rather than inside a
        /// monitor's work area.
        /// </summary>
        public static bool IsOnDockedTaskbar(Point physicalPoint)
        {
            foreach (MonitorInfo monitor in MonitorService.Shared.GetMonitors().Monitors)
            {
                if (!monitor.TaskbarDetected) continue;
                if (monitor.TaskbarPosition == TaskbarPosition.None) continue;

                Rectangle bounds = monitor.Bounds;
                if (!bounds.Contains(physicalPoint)) continue;

                // The point is on this monitor and outside its work area, i.e. in
                // the strip the taskbar reserves.
                return !monitor.WorkingArea.Contains(physicalPoint);
            }
            return false;
        }

        internal static MonitorService Shared { get; } = new MonitorService();

        private static MonitorQueryResult Enumerate()
        {
            var result = new MonitorQueryResult();
            bool autoHide = TryDetectAutoHide();

            try
            {
                NativeMethods.EnumDisplayMonitors(
                    IntPtr.Zero,
                    IntPtr.Zero,
                    delegate (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data)
                    {
                        AddMonitor(result, hMonitor, autoHide);
                        return true;
                    },
                    IntPtr.Zero);
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (Exception)
            {
            }

            // EnumDisplayMonitors always reports at least one monitor on a
            // session with a desktop, but a headless or session-0 run reports
            // none. Falling back to the primary screen keeps the popup code on a
            // single path instead of branching on "no monitors".
            if (result.Monitors.Count == 0)
            {
                result.Monitors.Add(new MonitorInfo
                {
                    Id = "DEFAULT",
                    DeviceName = @"\\.\DISPLAY1",
                    Bounds = new Rectangle(0, 0, 1920, 1080),
                    WorkingArea = new Rectangle(0, 0, 1920, 1040),
                    Primary = true,
                    ScaleFactor = 1d,
                    EnumerationIndex = 0,
                    TaskbarPosition = TaskbarPosition.Bottom,
                    TaskbarDetected = true,
                    Orientation = ScreenOrientation.Landscape
                });
            }

            if (result.Primary == null)
            {
                // Nothing claimed to be primary, which happens with a driver that
                // reports a stale flags field. The first enumerated monitor is
                // what Win32 itself falls back to.
                result.Monitors[0].Primary = true;
            }

            result.VirtualBounds = UnionOf(result.Monitors);
            return result;
        }

        private static void AddMonitor(MonitorQueryResult result, IntPtr hMonitor, bool autoHide)
        {
            NativeMethods.MONITORINFOEX info = default;
            info.cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>();

            if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
                return;

            Rectangle bounds = ToRectangle(info.rcMonitor);
            Rectangle work = ToRectangle(info.rcWork);

            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            double scale = DpiService.ScaleOfMonitor(hMonitor);
            string deviceName = info.szDevice ?? string.Empty;

            var monitor = new MonitorInfo
            {
                Id = MonitorGeometry.IdFor(deviceName),
                DeviceName = deviceName,
                Bounds = bounds,
                WorkingArea = work,
                Primary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                ScaleFactor = scale,
                DpiX = (int)Math.Round(96 * scale),
                DpiY = (int)Math.Round(96 * scale),
                Orientation = MonitorGeometry.OrientationOf(bounds),
                EnumerationIndex = result.Monitors.Count
            };

            ResolveTaskbar(monitor, autoHide);
            result.Monitors.Add(monitor);

            if (monitor.Primary)
                result.Primary = monitor;
        }

        /// <summary>
        /// Derives the taskbar state from the difference between a monitor's full
        /// bounds and its work area, per monitor.
        /// </summary>
        /// <remarks>
        /// This is the same signal the legacy <c>FindDockedTaskBars</c> used, but
        /// it is read for one monitor at a time so it cannot be attributed to the
        /// wrong screen. An auto-hidden taskbar reserves no space at all, which is
        /// reported honestly as <see cref="TaskbarPosition.AutoHide"/> instead of
        /// being flattened into "no taskbar anywhere".
        /// </remarks>
        private static void ResolveTaskbar(MonitorInfo monitor, bool autoHide)
        {
            Rectangle bounds = monitor.Bounds;
            Rectangle work = monitor.WorkingArea;

            int left = bounds.Left - work.Left;
            int top = bounds.Top - work.Top;
            int right = (bounds.Right - work.Right);
            int bottom = (bounds.Bottom - work.Bottom);

            int tol = Math.Max(1, bounds.Height / 200);

            if (bottom >= tol && bottom > top && bottom > left && bottom > right)
            {
                monitor.TaskbarPosition = TaskbarPosition.Bottom;
            }
            else if (top >= tol && top > bottom && top > left && top > right)
            {
                monitor.TaskbarPosition = TaskbarPosition.Top;
            }
            else if (left >= tol && left > right && left > top && left > bottom)
            {
                monitor.TaskbarPosition = TaskbarPosition.Left;
            }
            else if (right >= tol && right > left && right > top && right > bottom)
            {
                monitor.TaskbarPosition = TaskbarPosition.Right;
            }
            else if (autoHide)
            {
                monitor.TaskbarPosition = TaskbarPosition.AutoHide;
            }
            else
            {
                monitor.TaskbarPosition = TaskbarPosition.None;
            }

            monitor.TaskbarDetected = monitor.TaskbarPosition != TaskbarPosition.None
                                       && monitor.TaskbarPosition != TaskbarPosition.AutoHide;
            monitor.TaskbarAutoHide = autoHide && !monitor.TaskbarDetected;
        }

        private static bool TryDetectAutoHide()
        {
            try
            {
                return NativeMethods.IsAutoHideEnabled();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Rectangle ToRectangle(NativeMethods.RECT rect)
        {
            return new Rectangle(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
        }

        private static Rectangle UnionOf(IReadOnlyList<MonitorInfo> monitors)
        {
            if (monitors == null || monitors.Count == 0) return Rectangle.Empty;

            int left = int.MaxValue;
            int top = int.MaxValue;
            int right = int.MinValue;
            int bottom = int.MinValue;

            foreach (var monitor in monitors)
            {
                left = Math.Min(left, monitor.Bounds.Left);
                top = Math.Min(top, monitor.Bounds.Top);
                right = Math.Max(right, monitor.Bounds.Right);
                bottom = Math.Max(bottom, monitor.Bounds.Bottom);
            }

            return new Rectangle(left, top, right - left, bottom - top);
        }
    }
}