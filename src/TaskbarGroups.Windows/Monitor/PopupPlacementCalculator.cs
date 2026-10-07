using System;
using System.Collections.Generic;
using System.Drawing;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;

namespace TaskbarGroups.Windows.Monitor
{
    /// <summary>
    /// Works out where a popup goes, in pure arithmetic.
    /// </summary>
    /// <remarks>
    /// Deliberately free of Win32 calls so the placement matrix - single, dual and
    /// triple monitors, mixed resolutions, portrait displays, negative virtual
    /// screen origins, 100% to 200% scaling - can be tested exhaustively without
    /// needing that many physical displays. This is the direct replacement for
    /// <c>frmMain.SetLocation()</c>, whose index-based taskbar lookup threw
    /// <see cref="ArgumentOutOfRangeException"/> and whose hidden-taskbar branch
    /// compared a virtual-screen Y against a primary-monitor height.
    /// </remarks>
    public static class PopupPlacementCalculator
    {
        /// <summary>
        /// Places a popup of <paramref name="desiredSizeDip"/> on the monitor
        /// holding <paramref name="anchor"/>, inside that monitor's work area.
        /// </summary>
        /// <param name="request">Anchor, preferred size, margin and monitor preference.</param>
        /// <param name="monitors">All monitors, with their taskbar state resolved.</param>
        /// <param name="cursorFallback">Used when the preference and the anchor do not pick a monitor.</param>
        public static PopupPlacement Calculate(
            PopupPlacementRequest request,
            MonitorQueryResult monitors,
            Point cursorFallback)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (monitors == null) throw new ArgumentNullException(nameof(monitors));

            var placement = new PopupPlacement
            {
                Size = request.DesiredSize,
                WasClamped = false
            };

            MonitorInfo? monitor = monitors.Resolve(
                request.MonitorPreference,
                request.PreferredMonitorDeviceName,
                request.AnchorPoint,
                null);

            if (monitor == null)
            {
                // No display at all. Nothing sensible can be produced, so hand
                // back the requested size at the requested point rather than
                // throwing at a caller that is mid-render.
                placement.Location = request.AnchorPoint;
                return placement;
            }

            placement.MonitorId = monitor.Id;
            placement.MonitorDeviceName = monitor.DeviceName;

            Rectangle work = EffectiveWorkArea(monitor);
            double scale = monitor.ScaleFactor > 0 ? monitor.ScaleFactor : 1d;

            Size sizePx = Dpi.Scale(request.DesiredSize, scale);

            // Never larger than the work area, and never smaller than the
            // configured minimum unless the work area itself is smaller.
            int maxWidth = Math.Max(1, work.Width);
            int maxHeight = Math.Max(1, work.Height);

            if (sizePx.Width > maxWidth || sizePx.Height > maxHeight)
            {
                sizePx = new Size(Math.Min(sizePx.Width, maxWidth), Math.Min(sizePx.Height, maxHeight));
                placement.WasClamped = true;
            }

            int margin = Math.Max(0, Dpi.Scale(request.Margin, scale));

            Point desired = request.AnchorOnTaskbar
                ? FromTaskbar(request.AnchorPoint, monitor, sizePx, margin, scale)
                : AboveAnchor(request.AnchorPoint, sizePx, margin);

            int x = desired.X;
            int y = desired.Y;

            // Clamp into the work area. The popup always ends up fully visible on
            // one monitor, which is what stops a group from opening off-screen on
            // a lower-resolution secondary display.
            int minX = work.Left + margin;
            int maxX = work.Right - sizePx.Width - margin;
            int minY = work.Top + margin;
            int maxY = work.Bottom - sizePx.Height - margin;

            int clampedX = x < minX ? minX : (x > maxX ? maxX : x);
            int clampedY = y < minY ? minY : (y > maxY ? maxY : y);

            if (clampedX != x || clampedY != y) placement.WasClamped = true;

            // When the work area is too small even for the margin, fall back to
            // pinning to the work area origin rather than inverting the clamp.
            if (maxX < minX) clampedX = work.Left;
            if (maxY < minY) clampedY = work.Top;

            placement.Location = new Point(clampedX, clampedY);
            placement.Size = new Size(sizePx.Width, sizePx.Height);
            return placement;
        }

        /// <summary>
        /// The area a popup may occupy on a monitor: the work area, except when a
        /// taskbar is auto-hidden on that monitor and we are asked to keep clear of
        /// the reserved strip anyway.
        /// </summary>
        private static Rectangle EffectiveWorkArea(MonitorInfo monitor)
        {
            Rectangle work = monitor.WorkingArea;
            if (work.Width <= 0 || work.Height <= 0)
                return monitor.Bounds;
            return work;
        }

        /// <summary>
        /// Positions the popup inwards from whichever edge the taskbar is docked
        /// to, centred on the click. This is the behaviour a taskbar click has
        /// always had; what changed is that the edge comes from the monitor that
        /// was actually clicked.
        /// </summary>
        private static Point FromTaskbar(Point anchor, MonitorInfo monitor, Size size, int margin, double scale)
        {
            Rectangle work = EffectiveWorkArea(monitor);
            int centeredX = anchor.X - (size.Width / 2);

            switch (monitor.TaskbarPosition)
            {
                case TaskbarPosition.Top:
                    return new Point(centeredX, work.Top + margin);

                case TaskbarPosition.Left:
                    return new Point(work.Left + margin, anchor.Y - (size.Height / 2));

                case TaskbarPosition.Right:
                    return new Point(work.Right - size.Width - margin, anchor.Y - (size.Height / 2));

                case TaskbarPosition.Bottom:
                default:
                    return new Point(centeredX, work.Bottom - size.Height - margin);
            }
        }

        /// <summary>Positions the popup above and centred on the anchor point.</summary>
        private static Point AboveAnchor(Point anchor, Size size, int margin)
        {
            int gap = Math.Max(0, margin);
            return new Point(anchor.X - (size.Width / 2), anchor.Y - size.Height - gap);
        }
    }

    /// <summary>Physical/logical pixel conversions used by the placement calculator.</summary>
    internal static class Dpi
    {
        internal static int Scale(int dip, double scale)
        {
            if (scale <= 0) scale = 1d;
            return (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);
        }

        internal static Size Scale(Size dip, double scale)
        {
            return new Size(Scale(dip.Width, scale), Scale(dip.Height, scale));
        }
    }

    /// <summary>Works out a monitor's orientation and stable identifier.</summary>
    internal static class MonitorGeometry
    {
        internal static ScreenOrientation OrientationOf(Rectangle bounds)
        {
            if (bounds.Width == 0 || bounds.Height == 0) return ScreenOrientation.Unknown;
            if (bounds.Width > bounds.Height) return ScreenOrientation.Landscape;
            if (bounds.Height > bounds.Width) return ScreenOrientation.Portrait;
            return ScreenOrientation.Unknown;
        }

        /// <summary>
        /// A stable id from the device name. Used instead of an enumeration index
        /// so a persisted monitor reference survives a resolution change, a
        /// replug, or the user swapping the primary display.
        /// </summary>
        internal static string IdFor(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return "unknown";
            string trimmed = deviceName.Trim();
            if (trimmed.StartsWith(@"\\.\", StringComparison.Ordinal)) trimmed = trimmed.Substring(4);
            return trimmed.ToUpperInvariant();
        }
    }
}