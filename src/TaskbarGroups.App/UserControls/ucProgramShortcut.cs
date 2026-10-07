using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TaskbarGroups.App.Forms;
using TaskbarGroups.App.Services;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Windows.Dpi;

namespace TaskbarGroups.App.UserControls
{
    /// <summary>
    /// One editable shortcut row in the group editor.
    /// </summary>
    /// <remarks>
    /// Replaces the 1.x control. Its icon path was the P0 crash: it called
    /// <c>Icon.ExtractAssociatedIcon(...).ToBitmap()</c> on whatever the stored
    /// path was, with no existence check and no null check, and it resolved Store
    /// app icons synchronously on the UI thread. Here the icon comes from the
    /// shared cache, which never fails, and the row's status badge is derived from
    /// <see cref="ShortcutValidator.Classify"/> rather than from whether the file
    /// happens to exist.
    /// </remarks>
    public partial class ucProgramShortcut : UserControl
    {
        private const int RowHeight = 50;

        private readonly PictureBox _icon = new PictureBox();
        private readonly TextBox _name = new TextBox();
        private readonly Label _type = new Label();
        private readonly PictureBox _badge = new PictureBox();
        private readonly Button _up = new Button();
        private readonly Button _down = new Button();
        private readonly Button _delete = new Button();
        private readonly ToolTip _tips = new ToolTip();

        public ucProgramShortcut()
        {
            BuildLayout();
        }

        /// <summary>Set by the editor immediately after construction.</summary>
        public ServiceContainer Services { get; set; } = null!;

        public ShortcutItem Shortcut { get; set; } = null!;

        public int Position { get; set; }

        public frmGroup? Owner { get; set; }

        public bool IsSelected { get; set; }

        /// <summary>Raised when the row's delete button is pressed.</summary>
        public Action? OnDeleted { get; set; }

        /// <summary>Raised with -1 or +1 when the row is moved.</summary>
        public Action<int>? OnMoved { get; set; }

        private void BuildLayout()
        {
            Height = RowHeight;
            Width = 700;
            BackColor = Color.FromArgb(28, 28, 28);
            ForeColor = Color.FromArgb(240, 240, 240);
            Margin = Padding.Empty;

            _icon.SizeMode = PictureBoxSizeMode.Zoom;
            _icon.Size = new Size(32, 32);
            _icon.Location = new Point(8, 9);
            _icon.BackColor = Color.Transparent;

            _name.Location = new Point(48, 13);
            _name.Size = new Size(260, 26);
            _name.BorderStyle = BorderStyle.None;
            _name.BackColor = Color.FromArgb(28, 28, 28);
            _name.ForeColor = Color.FromArgb(240, 240, 240);
            _name.Font = new Font("Segoe UI", 9.5f);
            _name.TextChanged += (s, e) => Shortcut.Name = _name.Text;

            _type.Location = new Point(316, 16);
            _type.Size = new Size(110, 18);
            _type.ForeColor = Color.FromArgb(140, 140, 140);
            _type.Font = new Font("Segoe UI", 8.5f);

            _badge.Size = new Size(14, 14);
            _badge.Location = new Point(432, 18);

            Stepper(_up, "^");
            Stepper(_down, "¡");
            Stepper(_delete, "?");
            _up.Location = new Point(470, 12);
            _down.Location = new Point(496, 12);
            _delete.Location = new Point(522, 12);
            _delete.ForeColor = Color.FromArgb(255, 130, 130);

            _up.Click += (s, e) => OnMoved?.Invoke(-1);
            _down.Click += (s, e) => OnMoved?.Invoke(1);
            _delete.Click += (s, e) => OnDeleted?.Invoke();

            Click += (s, e) => Owner?.Select(this);
            _icon.Click += (s, e) => Owner?.Select(this);

            Controls.Add(_icon);
            Controls.Add(_name);
            Controls.Add(_type);
            Controls.Add(_badge);
            Controls.Add(_up);
            Controls.Add(_down);
            Controls.Add(_delete);
        }

        private static void Stepper(Button button, string text)
        {
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.Size = new Size(22, 22);
            button.BackColor = Color.FromArgb(38, 38, 38);
            button.ForeColor = Color.FromArgb(200, 200, 200);
            button.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            button.UseVisualStyleBackColor = false;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (Shortcut == null) return;

            double scale = DpiService.ScaleAtWindow(Handle);

            _name.Text = string.IsNullOrWhiteSpace(Shortcut.Name)
                ? DeriveName(Shortcut)
                : Shortcut.Name;

            _type.Text = DescribeType(Shortcut);

            _up.Enabled = Position > 0;
            _down.Enabled = true;

            LoadIcon(scale);
            LoadBadge();
            UpdateToolTip();
        }

        /// <summary>
        /// The icon, from the shared cache.
        /// </summary>
        /// <remarks>
        /// The P0 fix. The 1.x control called
        /// <c>Icon.ExtractAssociatedIcon(Shortcut.FilePath).ToBitmap()</c> on
        /// anything at all - including a folder, a Store app id and a deleted path -
        /// and dereferenced a null <see cref="Icon"/> when extraction failed. A
        /// folder shortcut threw <c>FileNotFoundException</c> out of a Load event
        /// handler and took the editor with it. The cache always returns a bitmap.
        /// </remarks>
        private void LoadIcon(double scale)
        {
            try
            {
                int size = Math.Max(16, (int)Math.Round(32 * scale));

                _icon.Image = Services.Icons.GetIcon(
                    Shortcut,
                    new IconRequest { LogicalSize = size, ScaleFactor = scale });
            }
            catch (Exception ex)
            {
                Services?.Logger.Log(SystemLogLevel.Error, "Icons",
                    "Icon failed for " + Shortcut.Name, ex);
            }
        }

        /// <summary>
        /// A small status marker, so a broken shortcut is visible without
        /// clicking it. Colour alone is never the only signal: the tooltip repeats
        /// it in words, which is also what a screen reader announces.
        /// </summary>
        private void LoadBadge()
        {
            ShortcutStatus status;

            try
            {
                status = Core.Validation.ShortcutValidator.Classify(Shortcut);
            }
            catch (Exception)
            {
                status = ShortcutStatus.Unknown;
            }

            switch (status)
            {
                case ShortcutStatus.Missing:
                    _badge.Image = TaskbarGroups.App.Properties.Resources.Error;
                    _badge.Visible = true;
                    break;

                case ShortcutStatus.PermissionDenied:
                    _badge.Image = TaskbarGroups.App.Properties.Resources.Error;
                    _badge.Visible = true;
                    break;

                case ShortcutStatus.InvalidShortcut:
                    _badge.Image = TaskbarGroups.App.Properties.Resources.Error;
                    _badge.Visible = true;
                    break;

                default:
                    _badge.Image = null;
                    _badge.Visible = false;
                    break;
            }
        }

        private void UpdateToolTip()
        {
            ShortcutStatus status = Core.Validation.ShortcutValidator.Classify(Shortcut);

            var lines = new System.Collections.Generic.List<string> { Shortcut.Target };

            switch (status)
            {
                case ShortcutStatus.Missing:
                    lines.Add("Missing: the target no longer exists.");
                    lines.Add("Click to select, then re-add it to the row.");
                    break;
                case ShortcutStatus.PermissionDenied:
                    lines.Add("Access denied. Run Taskbar Groups as administrator to inspect it.");
                    break;
                case ShortcutStatus.InvalidShortcut:
                    lines.Add("This .lnk is broken, or its target was removed.");
                    break;
                case ShortcutStatus.UriLauncher:
                    lines.Add("Resolved by Windows when it is opened.");
                    break;
            }

            if (Shortcut.RunAsAdministrator) lines.Add("Runs as administrator.");
            if (Shortcut.Type.ExecutesCode()) lines.Add("This runs a script when opened.");

            _tips.SetToolTip(this, string.Join(Environment.NewLine, lines));
            _tips.SetToolTip(_badge, string.Join(Environment.NewLine, lines));
        }

        /// <summary>
        /// A readable name when the user has not set one.
        /// </summary>
        /// <remarks>
        /// Prefers the shell's own display name, which is why the 1.x editor showed
        /// "Google Chrome" for a shortcut called chrome.lnk: it read the link's
        /// target rather than the file name.
        /// </remarks>
        internal static string DeriveName(ShortcutItem item)
        {
            try
            {
                if (item.Type == ShortcutType.Lnk && System.IO.File.Exists(item.Target))
                    return Windows.Shell.ShellLinkReader.GetDisplayName(item.Target);

                if (item.Type == ShortcutType.Uwp)
                    return item.Target;

                if (item.Type == ShortcutType.Folder)
                    return new DirectoryInfo(item.Target).Name;

                return Path.GetFileNameWithoutExtension(item.Target);
            }
            catch (Exception)
            {
                return item.Target;
            }
        }

        /// <summary>A short label describing what kind of thing this is.</summary>
        internal static string DescribeType(ShortcutItem item)
        {
            switch (item.Type)
            {
                case ShortcutType.Exe: return "Application";
                case ShortcutType.Lnk: return "Shortcut";
                case ShortcutType.Folder: return "Folder";
                case ShortcutType.Url: return "Web address";
                case ShortcutType.Uwp: return "Store app";
                case ShortcutType.Pwa: return "Web app";
                case ShortcutType.Steam: return "Steam game";
                case ShortcutType.Bat:
                case ShortcutType.Cmd: return "Script";
                case ShortcutType.Ps1: return "PowerShell";
                case ShortcutType.Epic:
                case ShortcutType.EA:
                case ShortcutType.Ubisoft:
                case ShortcutType.Gog:
                case ShortcutType.BattleNet:
                case ShortcutType.Xbox: return "Game";
                default: return "Unknown";
            }
        }

        /// <summary>Marks the row selected, for the editor's field panel.</summary>
        public void ApplySelected(bool selected)
        {
            BackColor = selected ? Color.FromArgb(45, 45, 45) : Color.FromArgb(28, 28, 28);
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tips.Dispose();

                // The icon comes from the shared cache and is shared with every
                // other row showing the same application, so it is detached rather
                // than disposed here.
                _icon.Image = null;
                _badge.Image = null;
            }

            base.Dispose(disposing);
        }
    }
}