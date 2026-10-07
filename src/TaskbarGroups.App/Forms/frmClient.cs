using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TaskbarGroups.App.Services;
using TaskbarGroups.App.UserControls;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Launchers;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;

namespace TaskbarGroups.App.Forms
{
    /// <summary>
    /// The dashboard: the group list, workspaces, search and the entry points to
    /// settings, discovery, backup and diagnostics.
    /// </summary>
    /// <remarks>
    /// Keeps the 1.x layout and every feature it had - group tiles with their
    /// shortcut icons, an add-group button, the edit affordance - and adds what
    /// 2.0 needs. The version check that used to run inside the constructor
    /// (<c>Task.Run(...).Result</c>, blocking the UI thread on the network) now
    /// happens after the window is up and its result is only used for a label.
    /// </remarks>
    public partial class frmClient : Form
    {
        private readonly ServiceContainer _services;
        private readonly List<ucCategoryPanel> _panels = new List<ucCategoryPanel>();

        private string _searchText = string.Empty;
        private AppCategory? _categoryFilter;

        public frmClient(ServiceContainer services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));

            InitializeComponent();

            Text = "Taskbar Groups";
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            Reload();
            UpdateHeader();

            // Fire-and-forget: the window is already interactive, so a slow or
            // unreachable update server cannot delay the first paint.
            _ = RefreshVersionLabelAsync();

            ReportPendingWorkspace();
        }

        /// <summary>Reloads every group from the database.</summary>
        public void Reload()
        {
            foreach (ucCategoryPanel panel in _panels)
            {
                try
                {
                    panel.IconBitmap?.Dispose();
                    panel.Dispose();
                }
                catch (Exception)
                {
                }
            }

            _panels.Clear();
            panelGroups.SuspendLayout();
            panelGroups.Controls.Clear();

            int offset = 0;

            foreach (Group group in _services.GroupRepository.GetAll())
            {
                List<ShortcutItem> shortcuts = _services.GroupRepository.GetShortcuts(group.Id);

                if (!MatchesFilter(group, shortcuts)) continue;

                var tile = new ucCategoryPanel(group, shortcuts)
                {
                    Services = _services,
                    OpenRequested = () => ShowGroupMenu(group)
                };

                tile.Location = new Point(0, offset);
                tile.Width = panelGroups.ClientSize.Width;
                offset += tile.Height;

                panelGroups.Controls.Add(tile);
                _panels.Add(tile);
            }

            panelGroups.ResumeLayout();

            UpdateEmptyState();
        }

        /// <summary>Applies the search box and the category filter.</summary>
        private bool MatchesFilter(Group group, List<ShortcutItem> shortcuts)
        {
            if (_categoryFilter.HasValue)
            {
                bool anyInCategory = shortcuts.Any(s =>
                    s.Type != ShortcutType.Unknown &&
                    Discovery.ApplicationDiscovery.Categorizer.Guess(s.Name, s.Target, s.Type) == _categoryFilter.Value);

                if (!anyInCategory) return false;
            }

            if (_searchText.Length == 0) return true;

            // Match the group name, the description, or any shortcut inside it, so
            // searching "steam" finds the group that contains Steam.
            if (group.Name.IndexOf(_searchText, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(group.Description) &&
                group.Description.IndexOf(_searchText, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;

            return shortcuts.Any(s =>
                (s.Name ?? string.Empty).IndexOf(_searchText, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                (s.Target ?? string.Empty).IndexOf(_searchText, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        private void UpdateEmptyState()
        {
            bool anyGroups = _services.GroupRepository.GetAll().Count > 0;

            if (anyGroups && _panels.Count == 0)
            {
                lblHelpTitle.Text = "No group matches the current search.";
                panelHelp.Visible = true;
            }
            else if (anyGroups)
            {
                lblHelpTitle.Text = "Click a group to add a taskbar shortcut";
                panelHelp.Visible = false;
            }
            else
            {
                lblHelpTitle.Text = "Press \"Add Taskbar group\" to get started";
                panelHelp.Visible = false;
            }

            panelActions.Top = panelGroups.Bottom + 20;
        }

        private void UpdateHeader()
        {
            int groups = _services.GroupRepository.GetAll().Count;
            int shortcuts = 0;
            foreach (Group group in _services.GroupRepository.GetAll())
                shortcuts += _services.GroupRepository.GetShortcuts(group.Id).Count;

            lblHeader.Text = groups == 1 ? "1 group, " + shortcuts + " shortcuts" : groups + " groups, " + shortcuts + " shortcuts";
        }

        private async Task RefreshVersionLabelAsync()
        {
            try
            {
                UpdateCheckResult result = await _services.Updates.CheckAsync();

                lblVersion.Text = result.UpdateAvailable
                    ? "v" + result.CurrentVersion + "  (update available: " + result.LatestVersion + ")"
                    : "v" + result.CurrentVersion;
            }
            catch (Exception)
            {
                lblVersion.Text = "v" + _services.Updates.CurrentVersion;
            }
        }

        private void ReportPendingWorkspace()
        {
            if (_services.MigrationReport == null) return;

            MigrationReport report = _services.MigrationReport;

            DialogResult answer = MessageBox.Show(this,
                report.GroupsImportedCount + " group(s) were imported from Taskbar Groups 1.x." +
                (string.IsNullOrEmpty(report.BackupArchivePath)
                    ? ""
                    : "\n\nA backup of the original files was written to:\n" + report.BackupArchivePath) +
                (report.GroupsImportedCount > 0
                    ? "\n\nThe original files were left in place."
                    : ""),
                "Migration complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            if (report.ShortcutsSkipped > 0 || report.UnsupportedFields.Count > 0)
            {
                // Not hidden: anything that could not be carried over is said out
                // loud, and the full report is on the diagnostics page.
                MessageBox.Show(this,
                    report.ShortcutsSkipped + " shortcut(s) could not be imported, and " +
                    report.UnsupportedFields.Count + " legacy setting(s) have no equivalent in 2.0.\n\n" +
                    "The full list is on the Diagnostics page.",
                    "Migration notes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Opens a group's popup, or asks what to do with its shortcut.</summary>
        private void ShowGroupMenu(Group group)
        {
            using (var menu = new ContextMenuStrip())
            {
                menu.Items.Add(Item("Open group", () => LaunchGroupPopup(group)));
                menu.Items.Add(Item("Open all", () => LaunchGroupAll(group)));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(Item("Edit group...", () => EditGroup(group)));
                menu.Items.Add(Item("Duplicate group", () => DuplicateGroup(group)));
                menu.Items.Add(Item("Show taskbar shortcut", () => ShowTaskbarShortcut(group)));
                menu.Items.Add(Item("Repair taskbar shortcut", () => RepairTaskbarShortcut(group)));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(Item("Delete group...", () => DeleteGroup(group)));

                menu.Show(Cursor.Position);
            }
        }

        private static ToolStripMenuItem Item(string text, Action action)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Taskbar Groups", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            return item;
        }

        private void LaunchGroupPopup(Group group)
        {
            try
            {
                // A new process rather than a window in this one, because the
                // AppUserModelID must differ per group for Windows to give each a
                // separate taskbar button.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _services.ExecutablePath,
                    Arguments = "\"" + GroupNameValidator.ToStorageName(group.Name) + "\"",
                    WorkingDirectory = Path.GetDirectoryName(_services.ExecutablePath) ?? string.Empty,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("The group could not be opened: " + ex.Message,
                    group.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void LaunchGroupAll(Group group)
        {
            if (!group.OpenAllEnabled)
            {
                MessageBox.Show(this,
                    "Launch all is switched off for \"" + group.Name + "\".\n\nEnable it in the group editor.",
                    "Launch all", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<ShortcutItem> shortcuts = _services.GroupRepository.GetShortcuts(group.Id);
            LaunchBatchResult batch = ((LauncherResolver)_services.Launchers).OpenAll(
                shortcuts,
                OpenAllOptions.FromSettings(_services.Settings),
                _services.CreateLaunchContext());

            if (batch.Failed > 0)
            {
                MessageBox.Show(this,
                    batch.Succeeded + " opened, " + batch.Failed + " could not be opened.\n\nSee the launcher log for details.",
                    group.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void EditGroup(Group group)
        {
            using (var editor = new frmGroup(_services, group))
            {
                editor.ShowDialog(this);
            }

            Reload();
            UpdateHeader();
        }

        private void DuplicateGroup(Group group)
        {
            string name = GroupNameValidator.ToDisplayName(group.Name) + " copy";
            int suffix = 2;

            while (_services.GroupRepository.GetByName(name) != null)
                name = GroupNameValidator.ToDisplayName(group.Name) + " copy " + suffix++;

            var copy = new Group
            {
                Name = name,
                Description = group.Description,
                IconId = group.IconId,
                Width = group.Width,
                Theme = group.Theme,
                Opacity = group.Opacity,
                CornerRadius = group.CornerRadius,
                ShadowEnabled = group.ShadowEnabled,
                BlurEnabled = group.BlurEnabled,
                AnimationEnabled = group.AnimationEnabled,
                OpenAllEnabled = group.OpenAllEnabled,
                SortMode = group.SortMode
            };

            _services.GroupRepository.Upsert(copy);

            List<ShortcutItem> shortcuts = _services.GroupRepository.GetShortcuts(group.Id);
            var cloned = new List<ShortcutItem>();
            foreach (ShortcutItem item in shortcuts)
            {
                ShortcutItem itemCopy = item.Clone();
                itemCopy.GroupId = copy.Id;
                cloned.Add(itemCopy);
            }

            _services.GroupRepository.ReplaceShortcuts(copy.Id, cloned);

            Reload();
            UpdateHeader();
        }

        /// <summary>Opens Explorer on the group's taskbar shortcut.</summary>
        private void ShowTaskbarShortcut(Group group)
        {
            string? shortcut = _services.Taskbar.FindGroupShortcut(
                group.Name, legacyUnderscoreName: false);

            if (shortcut == null)
            {
                MessageBox.Show(this,
                    "No taskbar shortcut exists for \"" + group.Name + "\".\n\nUse \"Repair taskbar shortcut\" to create one.",
                    group.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                // ProcessStartInfo with a shell verb, rather than building an
                // "/select,\"path\"" command string, which is what the legacy code
                // did and which breaks on a path containing a quote.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + shortcut + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, group.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RepairTaskbarShortcut(Group group)
        {
            string? icon = null;

            if (!string.IsNullOrWhiteSpace(group.IconId) && File.Exists(group.IconId))
                icon = group.IconId;

            if (!_services.Taskbar.CreateGroupShortcut(group.Name, _services.ExecutablePath, icon ?? string.Empty, out string error))
            {
                MessageBox.Show(this,
                    "The taskbar shortcut could not be created: " + error +
                    "\n\nCheck that the Shortcuts folder is writable.",
                    group.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(this,
                "The taskbar shortcut for \"" + group.Name + "\" was created.\n\n" +
                "Windows 11 does not let an application pin a shortcut to the taskbar by itself. " +
                "Open the group from here, then right-click its icon on the taskbar and choose " +
                "\"Pin to taskbar\".",
                group.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void DeleteGroup(Group group)
        {
            DialogResult confirm = MessageBox.Show(this,
                "Delete \"" + group.Name + "\" and its " +
                _services.GroupRepository.GetShortcuts(group.Id).Count + " shortcut(s)?\n\n" +
                "The taskbar shortcut is removed too. This cannot be undone.",
                "Delete group", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            _services.GroupRepository.Delete(group.Id);
            _services.Taskbar.DeleteGroupShortcut(group.Name, legacyUnderscoreName: false, out _);

            Reload();
            UpdateHeader();
        }

        private void AddGroup()
        {
            using (var editor = new frmGroup(_services, null))
            {
                editor.ShowDialog(this);
            }

            Reload();
            UpdateHeader();
        }

        private void ShowDiscovery()
        {
            using (var window = new frmDiscovery(_services))
            {
                window.ShowDialog(this);
            }
        }

        private void ShowWorkspaces()
        {
            using (var window = new frmWorkspaces(_services))
            {
                window.ShowDialog(this);
            }
        }

        private void ShowSettingsPage()
        {
            using (var window = new frmSettings(_services))
            {
                window.ShowDialog(this);
            }

            Reload();
        }

        private void ShowDiagnosticsPage()
        {
            using (var window = new frmDiagnostics(_services))
            {
                window.ShowDialog(this);
            }
        }

        private void ShowBackupPage()
        {
            using (var window = new frmBackup(_services))
            {
                window.ShowDialog(this);
            }
        }

        /// <summary>Opens the settings window. Called by the tray menu.</summary>
        public void ShowSettingsTab()
        {
            ShowSettingsPage();
        }

        /// <summary>Opens the diagnostics window. Called by the tray menu.</summary>
        public void ShowDiagnosticsTab()
        {
            ShowDiagnosticsPage();
        }

        private void OpenWorkspacesItem_Click(object sender, EventArgs e) => ShowWorkspaces();

        private void ExportBackup()
        {
            ShowBackupPage();
        }

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            _searchText = (txtSearch.Text ?? string.Empty).Trim();
            Reload();
        }

        private void CategoryFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cboCategory.SelectedIndex > 0 && cboCategory.SelectedItem is CategoryFilterItem filter)
            {
                _categoryFilter = filter.Category;
            }
            else
            {
                _categoryFilter = null;
            }

            Reload();
        }

        private void frmClient_Resize(object sender, EventArgs e)
        {
            foreach (Control control in panelGroups.Controls)
                control.Width = panelGroups.ClientSize.Width;

            panelActions.Top = panelGroups.Bottom + 20;
        }

    }
}