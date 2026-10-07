using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TaskbarGroups.App.Services;
using TaskbarGroups.App.UserControls;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Launchers;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Windows.Dpi;
using TaskbarGroups.Windows.Monitor;

namespace TaskbarGroups.App.Forms
{
    /// <summary>
    /// The group popup.
    /// </summary>
    /// <remarks>
    /// This is the window a taskbar shortcut opens. Two things changed materially
    /// from the version this replaces:
    /// <list type="bullet">
    /// <item>Positioning goes through <see cref="MonitorService"/> and
    /// <see cref="PopupPlacementCalculator"/> instead of <c>SetLocation</c>. The old
    /// code indexed a taskbar rectangle list with the screen enumeration counter,
    /// which threw <see cref="ArgumentOutOfRangeException"/> as soon as one monitor
    /// had no taskbar - a two-monitor setup with the taskbar on one of them. It
    /// also compared a virtual-screen Y against a primary-monitor height when the
    /// taskbar was hidden, which was wrong on any secondary display.</item>
    /// <item>Launching goes through the launcher resolver instead of
    /// <c>Process.Start</c> with <c>UseShellExecute = false</c>, which is why
    /// folders, documents and Store apps used to fail.</item>
    /// The keyboard behaviour is unchanged: 1-0 launch the first ten, Ctrl+Enter
    /// launches all when the group allows it.
    /// </remarks>
    public partial class frmMain : Form
    {
        private readonly ServiceContainer _services;
        private readonly string _groupName;
        private readonly Point _invocationPoint;

        private Color _hoverColor = Color.FromArgb(50, 50, 50);
        private bool _isLoading = true;

        /// <summary>Shortcut cells, in display order.</summary>
        public List<ucShortcut> ControlList { get; } = new List<ucShortcut>();

        internal ServiceContainer Services => _services;

        internal Color HoverColor => _hoverColor;

        public Group? Group { get; private set; }

        public List<ShortcutItem> Shortcuts { get; } = new List<ShortcutItem>();

        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="services">The application object graph.</param>
        /// <param name="groupName">Storage name of the group, as passed on the command line.</param>
        /// <param name="invocationPoint">Physical cursor position at launch, in the taskbar shortcut's process.</param>
        public frmMain(ServiceContainer services, string groupName, Point invocationPoint)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _groupName = groupName ?? string.Empty;
            _invocationPoint = invocationPoint;

            InitializeComponent();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;

            LoadGroup();
        }

        private void LoadGroup()
        {
            Group = FindGroup();

            if (Group == null)
            {
                // A shortcut whose group was deleted, or one left over from a
                // previous install. Closing quietly is the right behaviour: the
                // popup is transient and a modal error here would be invisible and
                // confusing.
                _services.Logger.Log(SystemLogLevel.Warning, "App",
                    "Group '" + _groupName + "' was not found; closing the popup.");

                BeginInvoke(new Action(Close));
                return;
            }

            ApplyAppearance();
            LoadShortcuts();
        }

        private Group? FindGroup()
        {
            Group? group = _services.GroupRepository.GetByName(
                Core.Validation.GroupNameValidator.ToDisplayName(_groupName));

            if (group != null) return group;

            // The taskbar shortcut stores the storage name; fall back to it in
            // case the group's display name has drifted from its folder name.
            return _services.GroupRepository.GetByName(_groupName);
        }

        private void ApplyAppearance()
        {
            if (Group == null) return;

            Color background = ResolveBackground(Group);
            BackColor = background;
            Opacity = ClampOpacity(Group.Opacity);

            // Hover is a fixed step from the background in whichever direction has
            // contrast, rather than the legacy unconditional -50 which made a dark
            // group's hover colour invisible.
            _hoverColor = HoverFor(background);

            // The window icon is cosmetic: the taskbar button uses the .lnk icon,
            // and the popup is borderless. It is only set when the group has one.
            if (!string.IsNullOrWhiteSpace(Group.IconId) && File.Exists(Group.IconId))
            {
                try
                {
                    using (Icon icon = new Icon(Group.IconId))
                    {
                        Icon = icon;
                    }
                }
                catch (Exception)
                {
                    // A missing or invalid group icon must not stop the popup.
                }
            }
        }

        internal static Color ResolveBackground(Group group)
        {
            switch (group.Theme)
            {
                case GroupTheme.Light:
                    return Color.FromArgb(230, 230, 230);

                case GroupTheme.Dark:
                case GroupTheme.Mica:
                case GroupTheme.Acrylic:
                    return Color.FromArgb(31, 31, 31);

                default:
                    return Color.FromArgb(31, 31, 31);
            }
        }

        /// <summary>
        /// Maps the legacy 0..100 scale onto a window opacity. The legacy formula,
        /// 1 - value/100, meant 10 became 0.9 and 50 became 0.5; a group set to 100
        /// was completely invisible, which is never what a user meant.
        /// </summary>
        internal static double ClampOpacity(double legacyValue)
        {
            if (legacyValue < 0d) legacyValue = 0d;
            if (legacyValue > 100d) legacyValue = 100d;

            // Treat the value as "how transparent" but never go fully invisible:
            // 100% maps to 20% opaque, which reads as a very translucent popup
            // rather than a window that has vanished.
            double opacity = 1d - (legacyValue / 100d) * 0.8d;
            return Math.Max(0.2d, Math.Min(1d, opacity));
        }

        internal static Color HoverFor(Color background)
        {
            // Perceived luminance, so a mid-tone background is handled correctly.
            double luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255d;

            int delta = 24;
            if (luminance > 0.5d)
            {
                return Color.FromArgb(
                    Math.Max(0, background.R - delta),
                    Math.Max(0, background.G - delta),
                    Math.Max(0, background.B - delta));
            }

            return Color.FromArgb(
                Math.Min(255, background.R + delta),
                Math.Min(255, background.G + delta),
                Math.Min(255, background.B + delta));
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            if (Group == null) return;

            BuildGrid();
            PositionWindow();

            _isLoading = false;

            // The first cell takes focus so the number keys work immediately,
            // which is what a user of the 1-0 shortcut expects.
            if (ControlList.Count > 0) ControlList[0].Focus();
        }

        /// <summary>
        /// Lays out the shortcut grid using the settings rather than the hardcoded
        /// 55/45/10/20 pixel values the legacy form used.
        /// </summary>
        private void BuildGrid()
        {
            if (Group == null) return;

            var ordered = ShortcutSortHelper.Order(Shortcuts, Group.SortMode).ToList();

            double scale = DpiService.ScaleAtPoint(_invocationPoint);
            int cellWidth = Math.Max(40, (int)Math.Round(55 * scale));
            int cellHeight = Math.Max(40, (int)Math.Round(55 * scale));
            int iconSize = Math.Max(16, (int)Math.Round(_services.Settings.GroupIconSize * scale));

            int columns = Math.Max(1, Math.Min(Group.Width, ordered.Count == 0 ? 1 : ordered.Count));

            // Lay the cells out before the window is sized, because the popup's
            // preferred size comes from the grid.
            int maxWidth = 0;

            for (int index = 0; index < ordered.Count; index++)
            {
                int column = index % columns;
                int row = index / columns;

                var cell = new ucShortcut
                {
                    Shortcut = ordered[index],
                    Position = index,
                    Owner = this,
                    IconRequest = new IconRequest { LogicalSize = iconSize, ScaleFactor = scale }
                };

                cell.Location = new Point(column * cellWidth, row * cellHeight);
                cell.Size = new Size(cellWidth, cellHeight);

                Controls.Add(cell);
                ControlList.Add(cell);

                maxWidth = Math.Max(maxWidth, (column + 1) * cellWidth);
            }

            int desiredWidth = ordered.Count == 0 ? 160 : maxWidth;
            int desiredHeight = ordered.Count == 0
                ? cellHeight
                : (((ordered.Count - 1) / columns) + 1) * cellHeight;

            Size = new Size(desiredWidth, desiredHeight);
        }

        /// <summary>
        /// Places the popup on the monitor the click happened on, inside that
        /// monitor's work area.
        /// </summary>
        private void PositionWindow()
        {
            try
            {
                var request = new PopupPlacementRequest
                {
                    AnchorPoint = _invocationPoint,
                    DesiredSize = Size,
                    MonitorPreference = Group?.MonitorPreference ?? MonitorPreference.FollowCursor,
                    PreferredMonitorDeviceName = Group?.PreferredMonitorDeviceName ?? string.Empty,
                    Margin = _services.Settings.GroupPopupOffset,
                    MinimumSize = new Size(120, 80)
                };

                PopupPlacement placement = _services.MonitorService.CalculatePopupPlacement(request);

                Location = placement.Location;

                if (placement.Size.Width > 0 && placement.Size.Height > 0 &&
                    (placement.Size.Width < Size.Width || placement.Size.Height < Size.Height))
                {
                    Size = placement.Size;
                }
            }
            catch (Exception ex)
            {
                // Fall back to the cursor-relative position rather than opening at
                // an arbitrary spot or not at all.
                _services.Logger.Log(SystemLogLevel.Error, "App", "Popup placement failed", ex);

                Location = new Point(
                    _invocationPoint.X - (Width / 2),
                    Math.Max(0, _invocationPoint.Y - Height - _services.Settings.GroupPopupGap));
            }
        }

        /// <summary>Launches one shortcut and reports the outcome in the tooltip.</summary>
        internal void Launch(ShortcutItem item)
        {
            if (item == null) return;

            try
            {
                LaunchResult result = _services.Launchers.Launch(item, _services.CreateLaunchContext());

                if (result.Success)
                {
                    _services.Logger.Log(SystemLogLevel.Information, "Launcher",
                        Group!.Name + " -> " + result.ResolvedTarget);
                }
                else
                {
                    // A failed launch closes the popup and reports on the
                    // dashboard instead of raising a modal dialog over a
                    // borderless window the user cannot easily dismiss.
                    _services.Logger.Log(SystemLogLevel.Warning, "Launcher",
                        Group!.Name + " -> " + result.ErrorMessage);

                    MessageBox.Show(this,
                        item.Name + "\n\n" + result.ErrorMessage,
                        "Could not open",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                _services.Logger.Log(SystemLogLevel.Error, "Launcher", "Launch threw", ex);
                MessageBox.Show(this, ex.Message, "Could not open", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Ctrl+Enter: launches every enabled shortcut, continuing past failures.
        /// </summary>
        internal void LaunchAll()
        {
            if (Group == null || Shortcuts.Count == 0) return;

            if (!Group.OpenAllEnabled)
            {
                MessageBox.Show(this,
                    "Launch all is switched off for this group. Enable it in the group editor.",
                    "Launch all", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int enabled = Shortcuts.Count(s => s.Enabled);
            if (enabled >= _services.Settings.OpenAllWarningThreshold && _services.Settings.ConfirmBeforeOpenAll)
            {
                DialogResult confirm = MessageBox.Show(this,
                    "Open " + enabled + " applications?",
                    "Launch all", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

                if (confirm != DialogResult.OK) return;
            }

            LaunchBatchResult batch = ((LauncherResolver)_services.Launchers).OpenAll(
                Shortcuts,
                OpenAllOptions.FromSettings(_services.Settings),
                _services.CreateLaunchContext());

            Close();

            if (batch.Failed > 0)
            {
                _services.Logger.Log(SystemLogLevel.Warning, "Launcher",
                    "Launch all: " + batch.Succeeded + " opened, " + batch.Failed + " failed.");
            }
        }

        private void frmMain_Deactivate(object sender, EventArgs e)
        {
            if (_isLoading) return;
            Close();
        }

        /// <summary>
        /// 1-0 select the first ten shortcuts, Ctrl+Enter launches everything.
        /// Preserved verbatim from 1.x.
        /// </summary>
        private void frmMain_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.Control)
            {
                LaunchAll();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void frmMain_KeyUp(object sender, KeyEventArgs e)
        {
            int index = NumericIndex(e.KeyCode);
            if (index < 0) return;

            e.Handled = true;

            // The legacy handler wrapped these in try/catch and did nothing on an
            // out-of-range index, so a group with three shortcuts silently swallowed
            // 4-0. Bounds are checked explicitly now.
            if (index >= ControlList.Count) return;

            Launch(ControlList[index].Shortcut);
        }

        /// <summary>
        /// Maps 1..0 to indices 0..9. Returns -1 for any other key.
        /// </summary>
        internal static int NumericIndex(Keys key)
        {
            switch (key)
            {
                case Keys.D1: case Keys.NumPad1: return 0;
                case Keys.D2: case Keys.NumPad2: return 1;
                case Keys.D3: case Keys.NumPad3: return 2;
                case Keys.D4: case Keys.NumPad4: return 3;
                case Keys.D5: case Keys.NumPad5: return 4;
                case Keys.D6: case Keys.NumPad6: return 5;
                case Keys.D7: case Keys.NumPad7: return 6;
                case Keys.D8: case Keys.NumPad8: return 7;
                case Keys.D9: case Keys.NumPad9: return 8;
                case Keys.D0: case Keys.NumPad0: return 9;
                default: return -1;
            }
        }

        private void LoadShortcuts()
        {
            Shortcuts.Clear();

            if (Group == null) return;

            try
            {
                Shortcuts.AddRange(_services.GroupRepository.GetShortcuts(Group.Id));
            }
            catch (Exception ex)
            {
                _services.Logger.Log(SystemLogLevel.Error, "App",
                    "Shortcuts for '" + Group.Name + "' could not be loaded", ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            // Each cell owns and disposes its own icon bitmap, so disposing the
            // controls is sufficient. Disposing them again here would free the same
            // bitmap twice, which GDI+ does not tolerate.
            base.Dispose(disposing);
        }
    }
}