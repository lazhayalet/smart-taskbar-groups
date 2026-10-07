using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TaskbarGroups.App.Services;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Windows.Dpi;

namespace TaskbarGroups.App.UserControls
{
    /// <summary>
    /// One group tile on the dashboard: the group icon, its shortcut icons, and
    /// the click target that opens the group menu.
    /// </summary>
    /// <remarks>
    /// Replaces the 1.x control, which built a <see cref="PictureBox"/> per
    /// shortcut with fixed pixel metrics (50px columns, 40px rows, 30x30 icons)
    /// and called
    /// <c>Process.Start("explorer.exe", "/select,\"" + path + "\"")</c> - command
    /// line concatenation with an unescaped path.
    ///
    /// Two behaviour notes worth keeping:
    /// <list type="bullet">
    /// <item>The legacy tile's "click" revealed the group's .lnk in Explorer so
    /// the user could pin it. That is still offered, but it is now one item in a
    /// menu alongside opening the group directly, because on Windows 11 that is
    /// more discoverable than opening Explorer and hoping the user knows the next
    /// step.</item>
    /// <item>Icons come from the shared cache at the size they will be drawn, so a
    /// group with twenty shortcuts opens instantly instead of extracting twenty
    /// shell icons on every dashboard load.</item>
    /// </list>
    /// </remarks>
    public partial class ucCategoryPanel : UserControl
    {
        private const int Columns = 8;
        private const int CellWidthDip = 40;
        private const int CellHeightDip = 34;
        private const int IconSizeDip = 24;

        private readonly Group _group;
        private readonly List<ShortcutItem> _shortcuts;
        private readonly FlowLayoutPanel _iconHost = new FlowLayoutPanel();
        private readonly PictureBox _groupIcon = new PictureBox();
        private readonly Label _title = new Label();

        public ucCategoryPanel(Group group, List<ShortcutItem> shortcuts)
        {
            _group = group ?? throw new ArgumentNullException(nameof(group));
            _shortcuts = shortcuts ?? new List<ShortcutItem>();

            Services = null!;
            InitializeComponent();

            double scale = DpiService.ScaleAtWindow(Handle);

            BuildTitle();
            BuildIcons(scale);

            Height = ComputeHeight();
        }

        /// <summary>Set immediately after construction by the dashboard.</summary>
        public ServiceContainer Services { get; set; }

        /// <summary>The group icon bitmap, owned and disposed by this control.</summary>
        public Bitmap? IconBitmap { get; private set; }

        /// <summary>Raised when the user clicks the tile.</summary>
        public Action? OpenRequested { get; set; }

        private void InitializeComponent()
        {
            SuspendLayout();

            _title.AutoSize = false;
            _title.Dock = DockStyle.Top;
            _title.Height = 34;
            _title.ForeColor = Color.FromArgb(240, 240, 240);
            _title.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            _title.TextAlign = ContentAlignment.MiddleLeft;
            _title.Padding = new Padding(8, 0, 0, 0);
            _title.Text = Core.Validation.GroupNameValidator.ToDisplayName(_group.Name);
            _title.Cursor = Cursors.Hand;

            _groupIcon.SizeMode = PictureBoxSizeMode.Zoom;
            _groupIcon.Size = new Size(32, 32);
            _groupIcon.Location = new Point(8, 4);
            _groupIcon.Cursor = Cursors.Hand;

            _iconHost.Dock = DockStyle.Fill;
            _iconHost.AutoScroll = false;
            _iconHost.WrapContents = false;
            _iconHost.FlowDirection = FlowDirection.LeftToRight;
            _iconHost.Padding = new Padding(0);

            BackColor = Color.FromArgb(24, 24, 24);
            ForeColor = Color.FromArgb(240, 240, 240);
            Padding = new Padding(4);
            Margin = new Padding(4);
            Width = 900;

            Controls.Add(_iconHost);
            Controls.Add(_title);

            ResumeLayout(false);
        }

        private void BuildTitle()
        {
            _title.Text = Core.Validation.GroupNameValidator.ToDisplayName(_group.Name);
            _title.Click += (s, e) => OpenRequested?.Invoke();
        }

        private void BuildIcons(double scale)
        {
            _iconHost.SuspendLayout();
            _iconHost.Controls.Clear();

            int iconSize = Math.Max(16, (int)Math.Round(IconSizeDip * scale));
            int cellWidth = Math.Max(24, (int)Math.Round(CellWidthDip * scale));
            int cellHeight = Math.Max(24, (int)Math.Round(CellHeightDip * scale));

            var request = new IconRequest { LogicalSize = iconSize, ScaleFactor = scale };

            foreach (ShortcutItem item in _shortcuts.Take(Columns))
            {
                var cell = new PictureBox
                {
                    Size = new Size(cellWidth, cellHeight),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Margin = Padding.Empty,
                    Tag = item,
                    Cursor = Cursors.Hand
                };

                try
                {
                    // An owned copy: the tile disposes it when it is disposed.
                    cell.Image = Services?.Icons.GetIcon(item, request);
                }
                catch (Exception)
                {
                    // The cache always returns something; a cell without an image is
                    // preferable to a tile that fails to render.
                }

                ToolTip tips = new ToolTip { InitialDelay = 200, ShowAlways = true };
                tips.SetToolTip(cell, BuildTooltip(item));

                cell.Click += (s, e) => OpenRequested?.Invoke();

                _iconHost.Controls.Add(cell);
            }

            if (_shortcuts.Count > Columns)
            {
                var more = new Label
                {
                    Text = "+" + (_shortcuts.Count - Columns),
                    AutoSize = false,
                    Size = new Size(cellWidth, cellHeight),
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.FromArgb(160, 160, 160),
                    Margin = Padding.Empty,
                    Font = new Font("Segoe UI", 9f)
                };

                _iconHost.Controls.Add(more);
            }

            _iconHost.ResumeLayout(false);
        }

        private static string BuildTooltip(ShortcutItem item)
        {
            string name = string.IsNullOrWhiteSpace(item.Name) ? item.Target : item.Name;

            var lines = new List<string> { name, item.Target };

            if (!item.Enabled) lines.Add("Disabled");
            if (item.RunAsAdministrator) lines.Add("Runs as administrator");

            try
            {
                ShortcutStatus status = Core.Validation.ShortcutValidator.Classify(item);
                switch (status)
                {
                    case ShortcutStatus.Missing:
                        lines.Add("Target is missing");
                        break;
                    case ShortcutStatus.PermissionDenied:
                        lines.Add("Access to the target is denied");
                        break;
                    case ShortcutStatus.InvalidShortcut:
                        lines.Add("This shortcut is broken");
                        break;
                    case ShortcutStatus.Disabled:
                        lines.Add("Disabled in the group editor");
                        break;
                }
            }
            catch (Exception)
            {
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Height needed for the title row plus one or two rows of shortcut icons.
        /// </summary>
        private int ComputeHeight()
        {
            int rows = _shortcuts.Count == 0 ? 0 : (int)Math.Ceiling(_shortcuts.Count / (double)Columns);
            return 34 + (rows * CellHeightDip) + 12;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            BackColor = Color.FromArgb(34, 34, 34);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            BackColor = Color.FromArgb(24, 24, 24);
            base.OnMouseLeave(e);
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { IconBitmap?.Dispose(); } catch (Exception) { }

                // Each cell's bitmap belongs to the tile, so it is disposed here
                // rather than merely detached.
                foreach (Control control in _iconHost.Controls)
                {
                    if (control is not PictureBox box) continue;

                    try
                    {
                        Image? image = box.Image;
                        box.Image = null;
                        image?.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }

                _iconHost.Dispose();
                _title.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}