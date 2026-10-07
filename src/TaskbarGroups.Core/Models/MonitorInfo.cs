using System;
using System.Drawing;

namespace TaskbarGroups.Core.Models
{
    /// <summary>
    /// A monitor as this application sees it.
    /// </summary>
    /// <remarks>
    /// The legacy popup found monitors by iterating <c>Screen.AllScreens</c> and
    /// pairing each one with a taskbar rectangle by enumeration index. This model
    /// is built by geometry instead: every monitor carries its own bounds, work
    /// area, scale and taskbar state, so no code downstream ever needs an index.
    /// </remarks>
    public sealed class MonitorInfo
    {
        public MonitorInfo()
        {
            Id = string.Empty;
            DeviceName = string.Empty;
            Bounds = Rectangle.Empty;
            WorkingArea = Rectangle.Empty;
            DpiX = 96;
            DpiY = 96;
            ScaleFactor = 1d;
            Orientation = Enums.ScreenOrientation.Unknown;
            TaskbarPosition = Enums.TaskbarPosition.None;
        }

        /// <summary>Stable identifier derived from the device name.</summary>
        public string Id { get; set; }

        /// <summary>Win32 display device name, e.g. <c>\\.\DISPLAY1</c>.</summary>
        public string DeviceName { get; set; }

        /// <summary>Full monitor bounds in physical pixels, virtual-screen coordinates.</summary>
        public Rectangle Bounds { get; set; }

        /// <summary>
        /// Bounds minus any docked taskbar, in physical pixels. A monitor with an
        /// auto-hidden taskbar reports the same rectangle as <see cref="Bounds"/>.
        /// </summary>
        public Rectangle WorkingArea { get; set; }

        public int DpiX { get; set; }

        public int DpiY { get; set; }

        /// <summary>DpiX / 96.0. Use this to convert between DIPs and physical pixels.</summary>
        public double ScaleFactor { get; set; }

        public bool Primary { get; set; }

        public Enums.ScreenOrientation Orientation { get; set; }

        /// <summary>True when this monitor has a visible docked taskbar.</summary>
        public bool TaskbarDetected { get; set; }

        public Enums.TaskbarPosition TaskbarPosition { get; set; }

        /// <summary>True when the taskbar is set to auto-hide on this monitor.</summary>
        public bool TaskbarAutoHide { get; set; }

        /// <summary>Index in the enumeration order. Informational only; never load-bearing.</summary>
        public int EnumerationIndex { get; set; }

        /// <summary>True when the monitor is the same physical device as <paramref name="other"/>.</summary>
        public bool IsSameDevice(MonitorInfo? other)
        {
            if (other == null) return false;
            if (!string.IsNullOrEmpty(Id) && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase))
                return true;
            return string.Equals(DeviceName, other.DeviceName, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0} [{1}] {2}x{3} @{4} {5}x{6}% taskbar={7}{8}",
                string.IsNullOrEmpty(DeviceName) ? "(unnamed)" : DeviceName,
                Id,
                Bounds.Width,
                Bounds.Height,
                Bounds.X,
                Bounds.Y,
                Math.Round(ScaleFactor * 100),
                TaskbarPosition,
                Primary ? " primary" : string.Empty);
        }
    }
}