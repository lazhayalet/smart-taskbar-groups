using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using System.Linq;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Windows.Dpi;
using TaskbarGroups.Windows.Interop;

namespace TaskbarGroups.Windows.WindowManagement
{
    /// <summary>
    /// Finds, moves and sizes top-level windows for the workspace manager.
    /// </summary>
    /// <remarks>
    /// Window placement is the reason this class takes physical pixels:
    /// <c>SetWindowPos</c> expects device coordinates, while a workspace is
    /// authored in device-independent pixels so the same layout reads correctly on
    /// a 100% and a 200% display. The conversion happens here and nowhere else.
    /// </remarks>
    public sealed class WindowManager : IWindowManager
    {
        public IntPtr FindWindow(string executableOrTitle, bool matchTitle = false)
        {
            if (string.IsNullOrWhiteSpace(executableOrTitle)) return IntPtr.Zero;

            IntPtr result = IntPtr.Zero;

            try
            {
                NativeMethods.EnumWindows(
                    delegate (IntPtr hWnd, IntPtr _)
                    {
                        if (!NativeMethods.IsWindowVisible(hWnd)) return true;

                        string title = GetWindowTitle(hWnd);
                        if (string.IsNullOrEmpty(title)) return true;

                        if (matchTitle)
                        {
                            if (title.IndexOf(executableOrTitle, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                result = hWnd;
                                return false;
                            }
                            return true;
                        }

                        string exe = GetExecutableName(hWnd);
                        if (string.IsNullOrEmpty(exe)) return true;

                        if (string.Equals(exe, executableOrTitle, StringComparison.OrdinalIgnoreCase) ||
                            exe.StartsWith(executableOrTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            result = hWnd;
                            return false;
                        }

                        return true;
                    },
                    IntPtr.Zero);
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }

            return result;
        }

        /// <summary>
        /// Waits for a window to appear. Launching an app and then immediately
        /// placing it is a race; this polls until the process has a visible
        /// top-level window or the timeout expires.
        /// </summary>
        public IntPtr WaitForWindow(string executableName, int timeoutMs, bool matchTitle = false)
        {
            if (string.IsNullOrWhiteSpace(executableName)) return IntPtr.Zero;

            IntPtr found = IntPtr.Zero;
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));
            Action poll = () => System.Threading.Thread.Sleep(120);

            while (DateTime.UtcNow < deadline)
            {
                found = FindWindow(executableName, matchTitle);
                if (found != IntPtr.Zero) return found;
                poll();
            }

            return found;
        }

        public bool ApplyPlacement(IntPtr window, WindowPlacement placement, double scaleFactor)
        {
            if (window == IntPtr.Zero || placement == null) return false;

            double scale = scaleFactor > 0 ? scaleFactor : 1d;

            try
            {
                int width = placement.Width > 0 ? DpiService.DpiToPixels(placement.Width, scale) : 0;
                int height = placement.Height > 0 ? DpiService.DpiToPixels(placement.Height, scale) : 0;
                int x = DpiService.DpiToPixels(placement.X, scale);
                int y = DpiService.DpiToPixels(placement.Y, scale);

                // Restore first: a minimised or maximised window ignores SetWindowPos,
                // so placing a maximised window would silently do nothing.
                Restore(window);

                const uint flags = NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER;

                bool ok = width > 0 && height > 0
                    ? NativeMethods.SetWindowPos(window, IntPtr.Zero, x, y, width, height, flags)
                    : NativeMethods.SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, flags);

                if (!ok) return false;

                switch (placement.WindowState)
                {
                    case WindowStatePreference.Maximized:
                        NativeMethods.ShowWindow(window, NativeMethods.SW_SHOWMAXIMIZED);
                        break;
                    case WindowStatePreference.Minimized:
                        NativeMethods.ShowWindow(window, NativeMethods.SW_SHOWMINIMIZED);
                        break;
                    default:
                        NativeMethods.ShowWindow(window, NativeMethods.SW_SHOWNORMAL);
                        break;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public IReadOnlyList<WindowPlacement> ListVisibleWindows()
        {
            var results = new List<WindowPlacement>();

            try
            {
                NativeMethods.EnumWindows(
                    delegate (IntPtr hWnd, IntPtr _)
                    {
                        if (!NativeMethods.IsWindowVisible(hWnd)) return true;

                        string title = GetWindowTitle(hWnd);
                        if (string.IsNullOrWhiteSpace(title)) return true;

                        string exe = GetExecutableName(hWnd);
                        if (string.IsNullOrWhiteSpace(exe)) return true;

                        if (!NativeMethods.GetWindowRect(hWnd, out NativeMethods.RECT rect)) return true;

                        int width = rect.Right - rect.Left;
                        int height = rect.Bottom - rect.Top;
                        if (width <= 0 || height <= 0) return true;

                        results.Add(new WindowPlacement
                        {
                            Executable = exe,
                            ProcessMatch = title,
                            X = rect.Left,
                            Y = rect.Top,
                            Width = width,
                            Height = height,
                            WindowState = NativeMethods.IsZoomed(hWnd)
                                ? WindowStatePreference.Maximized
                                : NativeMethods.IsIconic(hWnd)
                                    ? WindowStatePreference.Minimized
                                    : WindowStatePreference.Normal,
                            CapturedAt = DateTimeOffset.Now
                        });

                        return true;
                    },
                    IntPtr.Zero);
            }
            catch (Exception)
            {
                return results;
            }

            return results;
        }

        /// <summary>Captures the current on-screen windows as a workspace layout.</summary>
        public WindowPlacement[] CaptureCurrentLayout()
        {
            return ListVisibleWindows().ToArray();
        }

        /// <summary>Restores a minimised or maximised window to normal so it can be moved.</summary>
        private static void Restore(IntPtr window)
        {
            try
            {
                if (NativeMethods.IsIconic(window))
                {
                    NativeMethods.ShowWindow(window, NativeMethods.SW_SHOWNORMAL);
                }
            }
            catch (Exception)
            {
            }
        }

        internal static string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                var buffer = new StringBuilder(512);
                NativeMethods.GetWindowTextW(hWnd, buffer, buffer.Capacity);
                return buffer.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        internal static string GetClassName(IntPtr hWnd)
        {
            try
            {
                var buffer = new StringBuilder(256);
                NativeMethods.GetClassNameW(hWnd, buffer, buffer.Capacity);
                return buffer.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>Process image name for the window's owning process.</summary>
        internal static string GetExecutableName(IntPtr hWnd)
        {
            try
            {
                NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0) return string.Empty;

                using (Process process = Process.GetProcessById((int)pid))
                {
                    string name = process.ProcessName;
                    return string.IsNullOrEmpty(name) ? string.Empty : name + ".exe";
                }
            }
            catch (Exception)
            {
                // Access denied on a protected process, or the process exited
                // between the window enumeration and this call.
                return string.Empty;
            }
        }

        /// <summary>Brings a window to the front without activating the whole process.</summary>
        public static void BringToFront(IntPtr window)
        {
            if (window == IntPtr.Zero) return;

            try
            {
                if (NativeMethods.IsIconic(window))
                {
                    NativeMethods.ShowWindow(window, NativeMethods.SW_SHOWNORMAL);
                }

                NativeMethods.SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOOWNERZORDER);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// Turns an anchor plus a work area into concrete coordinates.
    /// </summary>
    /// <remarks>
    /// Kept separate from the window plumbing so the geometry - the part that has
    /// to behave on a portrait display, an ultrawide and a half-screen tile - can
    /// be reasoned about and tested on its own.
    /// </remarks>
    public static class WindowPlacementCalculator
    {
        /// <summary>Computes a rectangle for an item, in logical pixels.</summary>
        public static Rectangle Resolve(Rectangle workAreaDip, WorkspaceItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            int width = item.Width > 0 ? item.Width : Math.Max(240, workAreaDip.Width / 2);
            int height = item.Height > 0 ? item.Height : Math.Max(160, workAreaDip.Height / 2);

            if (width > workAreaDip.Width) width = workAreaDip.Width;
            if (height > workAreaDip.Height) height = workAreaDip.Height;

            // Explicit coordinates win over the anchor, which is what lets a saved
            // layout round-trip exactly.
            if (item.X != 0 || item.Y != 0)
            {
                return Clamp(workAreaDip, new Rectangle(item.X, item.Y, width, height));
            }

            int halfWidth = workAreaDip.Width / 2;
            int halfHeight = workAreaDip.Height / 2;

            Rectangle candidate;
            switch (item.NormalizedPlacement)
            {
                case PlacementAnchor.Left:
                case PlacementAnchor.LeftHalf:
                    candidate = new Rectangle(workAreaDip.Left, workAreaDip.Top, halfWidth, workAreaDip.Height);
                    break;

                case PlacementAnchor.Right:
                case PlacementAnchor.RightHalf:
                    candidate = new Rectangle(workAreaDip.Left + halfWidth, workAreaDip.Top, workAreaDip.Width - halfWidth, workAreaDip.Height);
                    break;

                case PlacementAnchor.Top:
                case PlacementAnchor.TopHalf:
                    candidate = new Rectangle(workAreaDip.Left, workAreaDip.Top, workAreaDip.Width, halfHeight);
                    break;

                case PlacementAnchor.Bottom:
                case PlacementAnchor.BottomHalf:
                    candidate = new Rectangle(workAreaDip.Left, workAreaDip.Top + halfHeight, workAreaDip.Width, workAreaDip.Height - halfHeight);
                    break;

                case PlacementAnchor.TopLeft:
                    candidate = new Rectangle(workAreaDip.Left, workAreaDip.Top, halfWidth, halfHeight);
                    break;

                case PlacementAnchor.TopRight:
                    candidate = new Rectangle(workAreaDip.Left + halfWidth, workAreaDip.Top, workAreaDip.Width - halfWidth, halfHeight);
                    break;

                case PlacementAnchor.BottomLeft:
                    candidate = new Rectangle(workAreaDip.Left, workAreaDip.Top + halfHeight, halfWidth, workAreaDip.Height - halfHeight);
                    break;

                case PlacementAnchor.BottomRight:
                    candidate = new Rectangle(workAreaDip.Left + halfWidth, workAreaDip.Top + halfHeight, workAreaDip.Width - halfWidth, workAreaDip.Height - halfHeight);
                    break;

                case PlacementAnchor.Center:
                default:
                    candidate = new Rectangle(
                        workAreaDip.Left + ((workAreaDip.Width - width) / 2),
                        workAreaDip.Top + ((workAreaDip.Height - height) / 2),
                        width,
                        height);
                    break;
            }

            return Clamp(workAreaDip, candidate);
        }

        /// <summary>Keeps a rectangle inside a work area.</summary>
        public static Rectangle Clamp(Rectangle workArea, Rectangle desired)
        {
            int width = Math.Min(desired.Width, workArea.Width);
            int height = Math.Min(desired.Height, workArea.Height);

            int x = desired.X;
            int y = desired.Y;

            if (x + width > workArea.Right) x = workArea.Right - width;
            if (y + height > workArea.Bottom) y = workArea.Bottom - height;
            if (x < workArea.Left) x = workArea.Left;
            if (y < workArea.Top) y = workArea.Top;

            return new Rectangle(x, y, width, height);
        }
    }
}