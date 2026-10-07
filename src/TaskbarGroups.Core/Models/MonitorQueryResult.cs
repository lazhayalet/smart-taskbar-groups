using System;
using System.Collections.Generic;
using System.Drawing;

namespace TaskbarGroups.Core.Models
{
    /// <summary>
    /// Result of a monitor/working-area lookup.
    /// </summary>
    /// <remarks>
    /// Nothing downstream of this type is allowed to use a monitor index. The
    /// whole point of this object is that a monitor is identified by its device
    /// name, which survives a reboot, a resolution change and a rearrangement of
    /// the display settings.
    /// </remarks>
    public sealed class MonitorQueryResult
    {
        public MonitorQueryResult()
        {
            Monitors = new List<MonitorInfo>();
            VirtualBounds = Rectangle.Empty;
        }

        public List<MonitorInfo> Monitors { get; }

        public MonitorInfo? Primary { get; set; }

        /// <summary>Union of all monitor bounds; the coordinate space everything else lives in.</summary>
        public Rectangle VirtualBounds { get; set; }

        /// <summary>
        /// The monitor containing a physical point, falling back to the monitor
        /// with the largest overlap, then to the primary. Never returns null when
        /// at least one monitor exists.
        /// </summary>
        public MonitorInfo? ByPoint(Point physicalPoint)
        {
            MonitorInfo? best = null;
            long bestArea = -1;

            foreach (var monitor in Monitors)
            {
                if (monitor.Bounds.Contains(physicalPoint))
                    return monitor;

                // Containment fails on a gap between two monitors, or when a point
                // lands on a rounding seam. Largest overlap is the sensible answer.
                long overlap = OverlapArea(monitor.Bounds, new Rectangle(physicalPoint, new Size(1, 1)));
                if (overlap > bestArea)
                {
                    bestArea = overlap;
                    best = monitor;
                }
            }

            return best ?? Primary;
        }

        /// <summary>Monitor whose work area contains the point, else whose bounds do, else primary.</summary>
        public MonitorInfo? ByWorkAreaPoint(Point physicalPoint)
        {
            foreach (var monitor in Monitors)
            {
                if (monitor.WorkingArea.Contains(physicalPoint))
                    return monitor;
            }
            return ByPoint(physicalPoint);
        }

        public MonitorInfo? ByDeviceName(string? deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return null;
            foreach (var monitor in Monitors)
            {
                if (string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                    return monitor;
            }
            return null;
        }

        public MonitorInfo? ById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            foreach (var monitor in Monitors)
            {
                if (string.Equals(monitor.Id, id, StringComparison.OrdinalIgnoreCase))
                    return monitor;
            }
            return null;
        }

        /// <summary>Resolves a preference to a monitor, always returning something usable.</summary>
        public MonitorInfo? Resolve(Enums.MonitorPreference preference, string deviceName, Point cursor, Point? invokingWindow)
        {
            switch (preference)
            {
                case Enums.MonitorPreference.Primary:
                    return Primary ?? (Monitors.Count > 0 ? Monitors[0] : null);

                case Enums.MonitorPreference.Specific:
                case Enums.MonitorPreference.LastUsed:
                    MonitorInfo? named = ByDeviceName(deviceName);
                    if (named != null) return named;
                    // A monitor that was unplugged must not break the popup; fall
                    // through to the cursor-based behaviour rather than throwing.
                    break;
            }

            if (invokingWindow.HasValue)
            {
                MonitorInfo? owner = ByPoint(invokingWindow.Value);
                if (owner != null) return owner;
            }

            return ByPoint(cursor) ?? Primary ?? (Monitors.Count > 0 ? Monitors[0] : null);
        }

        internal static long OverlapArea(Rectangle a, Rectangle b)
        {
            int left = Math.Max(a.Left, b.Left);
            int right = Math.Min(a.Right, b.Right);
            int top = Math.Max(a.Top, b.Top);
            int bottom = Math.Min(a.Bottom, b.Bottom);
            if (right <= left || bottom <= top) return 0;
            return (long)(right - left) * (bottom - top);
        }
    }

    /// <summary>Where a popup window should be drawn, and on which monitor.</summary>
    public sealed class PopupPlacement
    {
        public PopupPlacement()
        {
            MonitorId = string.Empty;
            MonitorDeviceName = string.Empty;
            Size = System.Drawing.Size.Empty;
        }

        /// <summary>Final physical-pixel position. Always inside the chosen work area.</summary>
        public Point Location { get; set; }

        /// <summary>Size the popup should adopt; shrunk from the request if it did not fit.</summary>
        public Size Size { get; set; }

        /// <summary>
        /// Right edge in physical pixels.
        /// </summary>
        /// <remarks>
        /// A <see cref="Point"/> has no Right, and every caller that has to check
        /// "is this inside the work area" needs it. Exposing it here keeps that
        /// arithmetic from being re-derived differently at each call site.
        /// </remarks>
        public int Right => Location.X + Size.Width;

        /// <summary>Bottom edge in physical pixels.</summary>
        public int Bottom => Location.Y + Size.Height;

        public string MonitorId { get; set; }

        public string MonitorDeviceName { get; set; }

        /// <summary>True when the popup had to be shrunk or moved to stay on screen.</summary>
        public bool WasClamped { get; set; }
    }
}