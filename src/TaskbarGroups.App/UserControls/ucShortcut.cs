using System;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGroups.App.Forms;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;

namespace TaskbarGroups.App.UserControls
{
    /// <summary>
    /// One shortcut cell inside a group popup.
    /// </summary>
    /// <remarks>
    /// Replaces the 1.x control, which drew a fixed 32x32 icon into a 25x30 picture
    /// box using <c>ImageLayout.Stretch</c> - a resample by the layout engine that
    /// is the visible cause of the blurry HiDPI complaint. The icon is now produced
    /// at the exact pixel size it will be drawn at, from the shared cache, and the
    /// cell owns and disposes it.
    ///
    /// Launching is delegated to the owner form so there is exactly one launch
    /// path; the legacy control and the legacy form each had their own, and they
    /// disagreed about Store apps.
    /// </remarks>
    public partial class ucShortcut : UserControl
    {
        private bool _hovered;

        public ucShortcut()
        {
            InitializeComponent();
        }

        /// <summary>The item this cell represents.</summary>
        public ShortcutItem Shortcut { get; set; } = null!;

        /// <summary>Index within the group's display order, used by the 1-0 hotkeys.</summary>
        public int Position { get; set; }

        /// <summary>Popup that owns this cell.</summary>
        public frmMain? Owner { get; set; }

        /// <summary>Size and scale the icon should be produced at.</summary>
        public IconRequest IconRequest { get; set; } = new IconRequest();

        /// <summary>
        /// The icon bitmap. Owned by this control and disposed in its Dispose.
        /// </summary>
        public Bitmap? IconBitmap { get; private set; }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (Shortcut == null) return;

            try
            {
                // The cache hands out an owned copy per call, so this control keeps
                // it and disposes it in its own Dispose - the same lifetime a caller
                // would expect from any method that returns a Bitmap.
                IconBitmap = Owner?.Services.Icons.GetIcon(Shortcut, IconRequest);
                picIcon.Image = IconBitmap;
            }
            catch (Exception ex)
            {
                // The icon cache is total by construction, but a control should not
                // be the thing that fails if that ever changes.
                Owner?.Services.Logger.Log(SystemLogLevel.Error, "Icons",
                    "Icon could not be shown for " + Shortcut.Name, ex);
            }

            UpdateToolTip();
            ApplyBackColor();
        }

        private void UpdateToolTip()
        {
            string name = string.IsNullOrWhiteSpace(Shortcut.Name) ? Shortcut.Target : Shortcut.Name;
            string detail = Shortcut.Target;

            // A disabled item and a missing target are both worth saying out loud,
            // because otherwise the cell looks identical to a working one until it
            // silently does nothing.
            if (!Shortcut.Enabled) detail += Environment.NewLine + "Disabled in the group editor.";

            try
            {
                var status = Core.Validation.ShortcutValidator.Classify(Shortcut);
                if (status == Core.Enums.ShortcutStatus.Missing)
                    detail += Environment.NewLine + "Target is missing.";
                else if (status == Core.Enums.ShortcutStatus.PermissionDenied)
                    detail += Environment.NewLine + "Access to the target is denied.";
            }
            catch (Exception)
            {
            }

            toolTip.SetToolTip(this, name + Environment.NewLine + detail);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            ApplyBackColor();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            ApplyBackColor();
            base.OnMouseLeave(e);
        }

        private void ApplyBackColor()
        {
            if (_hovered && Owner != null)
                BackColor = Owner.HoverColor;
            else
                BackColor = Color.Transparent;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Clicked?.Invoke(this, e);
        }

        /// <summary>Raised when the cell is clicked; the popup performs the launch.</summary>
        public event EventHandler? Clicked;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // The image reference is dropped first so no paint can pick the
                // bitmap up between the control releasing it and it being freed.
                picIcon.Image = null;

                IconBitmap?.Dispose();
                IconBitmap = null;
            }

            base.Dispose(disposing);
        }
    }
}
