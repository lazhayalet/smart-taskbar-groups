using System;
using System.Drawing;
using System.Windows.Forms;

using TaskbarGroups.Core.Models;

namespace TaskbarGroups.App.Services
{
    /// <summary>
    /// Helpers for finding the monitor a control is on.
    /// </summary>
    /// <remarks>
    /// Windows Forms' <see cref="Screen.FromControl(Control)"/> returns the
    /// nearest screen by bounds, which is right for most cases but does not know
    /// about per-monitor scaling or the work area. These helpers go through the
    /// monitor service instead.
    ///
    /// Lives in the app project, not in Core: Core targets plain net8.0 and must not
    /// reference System.Windows.Forms.
    /// </remarks>
    public static class ControlPosition
    {
        /// <summary>
        /// The control's top-left corner in physical screen coordinates.
        /// </summary>
        /// <remarks>
        /// <c>Control.Left</c>/<c>Top</c> are relative to the parent and in logical
        /// units at the parent's scale; <c>PointToScreen</c> walks the whole chain
        /// and applies each level's scaling, which is what makes this correct for a
        /// control nested inside a scaled container.
        /// </remarks>
        public static Point PointOf(this Control control)
        {
            try
            {
                return control.PointToScreen(Point.Empty);
            }
            catch (Exception)
            {
                return Point.Empty;
            }
        }

        /// <summary>The control's centre in physical screen coordinates.</summary>
        public static Point CenterOf(this Control control)
        {
            try
            {
                return control.PointToScreen(new Point(
                    Math.Max(0, control.Width / 2),
                    Math.Max(0, control.Height / 2)));
            }
            catch (Exception)
            {
                return PointOf(control);
            }
        }
    }
}