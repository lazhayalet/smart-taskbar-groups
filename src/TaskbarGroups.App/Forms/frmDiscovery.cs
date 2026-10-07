using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Forms;
using TaskbarGroups.App.Services;
using TaskbarGroups.App.Views;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Groups.SmartGroups;
using TaskbarGroups.Discovery.ApplicationDiscovery;

namespace TaskbarGroups.App.Forms
{
    /// <summary>
    /// Finds installed applications and turns them into a group.
    /// </summary>
    /// <remarks>
    /// Discovery is asynchronous and cancellable because a full scan is slow and
    /// some users only want their Start Menu. Results are filtered in memory, so
    /// typing in the filter never touches the disk.
    ///
    /// The "smart group" flow takes the same results and a rule rather than a
    /// hand-picked list, which is what makes "all my development tools" possible
    /// without selecting thirty applications by hand.
    /// </remarks>
    public partial class frmDiscovery : Form
    {
        private readonly ServiceContainer _services;
        private readonly List<DiscoveredApplication> _all = new List<DiscoveredApplication>();
        private CancellationTokenSource? _scan;

        public frmDiscovery(ServiceContainer services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            InitializeComponent();

            PopulateCategories();
            PopulatePresets();
        }

        private void PopulateCategories()
        {
            cboCategory.Items.Add(new CategoryEntry(null, "All categories"));
            foreach (AppCategory category in Categorizer.All)
                cboCategory.Items.Add(new CategoryEntry(category, category.ToString()));
            cboCategory.SelectedIndex = 0;
        }

        private void PopulatePresets()
        {
            foreach (SmartGroupRule rule in _services.SmartGroups.PresetRules())
                cboPreset.Items.Add(new PresetEntry(rule));
        }

        private async void Scan_Click(object sender, EventArgs e)
        {
            _scan?.Cancel();
            _scan?.Dispose();
            _scan = new CancellationTokenSource();

            btnScan.Enabled = false;
            btnCancel.Enabled = true;
            lblProgress.Text = "Scanning...";
            btnCreateGroup.Enabled = false;

            var options = new DiscoveryOptions
            {
                IncludeStartMenu = true,
                IncludeDesktop = chkDesktop.Checked,
                IncludeProgramFiles = chkProgramFiles.Checked,
                IncludeUwp = chkStoreApps.Checked,
                IncludePwa = chkPwa.Checked,
                IncludeSteam = chkSteam.Checked
            };

            var progress = new Progress<string>(text => lblProgress.Text = text);

            try
            {
                IReadOnlyList<DiscoveredApplication> found =
                    await _services.Discovery.DiscoverAsync(options, progress, _scan.Token);

                _all.Clear();
                _all.AddRange(found);

                ApplyFilter();

                lblProgress.Text = found.Count + " application(s) found.";
                btnCreateGroup.Enabled = _all.Count > 0;
            }
            catch (OperationCanceledException)
            {
                lblProgress.Text = "Cancelled.";
            }
            catch (Exception ex)
            {
                _services.Logger.Log(SystemLogLevel.Error, "Discovery", "Scan failed", ex);
                lblProgress.Text = "The scan failed: " + ex.Message;
            }
            finally
            {
                btnScan.Enabled = true;
                btnCancel.Enabled = false;
            }
        }

        private void ApplyFilter()
        {
            string term = (txtFilter.Text ?? string.Empty).Trim();

            AppCategory? category = cboCategory.SelectedItem is CategoryEntry entry ? entry.Category : null;

            List<DiscoveredApplication> filtered = _all.Where(application =>
            {
                if (category.HasValue && application.Category != category.Value) return false;
                if (term.Length == 0) return true;

                return application.DisplayName.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0
                       || (application.Target ?? string.Empty).IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0
                       || application.Category.ToString().IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
            }).ToList();

            lstResults.BeginUpdate();
            lstResults.Items.Clear();

            foreach (DiscoveredApplication application in filtered.OrderBy(a => a.Category).ThenBy(a => a.DisplayName))
            {
                var item = new ListViewItem(new[]
                {
                    application.DisplayName,
                    application.Category.ToString(),
                    application.Type.ToString(),
                    application.Source.ToString()
                });

                item.Tag = application;
                lstResults.Items.Add(item);
            }

            lstResults.EndUpdate();

            lblProgress.Text = filtered.Count + " of " + _all.Count + " shown.";
        }

        private void Cancel_Click(object sender, EventArgs e)
        {
            _scan?.Cancel();
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            if (_all.Count > 0) ApplyFilter();
        }

        private void lstResults_DoubleClick(object sender, EventArgs e)
        {
            AddSelectedToNewGroup();
        }

        private void CreateGroup_Click(object sender, EventArgs e)
        {
            AddSelectedToNewGroup();
        }

        /// <summary>
        /// Builds a group from the selected applications.
        /// </summary>
        /// <remarks>
        /// Each entry is resolved through the shortcut resolver, so the group gets
        /// the same typed items whether they came from here, from a file dialog or
        /// from a drag and drop.
        /// </remarks>
        private void AddSelectedToNewGroup()
        {
            if (lstResults.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select at least one application first.", "Discovery",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var chosen = new List<DiscoveredApplication>();
            foreach (ListViewItem item in lstResults.SelectedItems)
            {
                if (item.Tag is DiscoveredApplication application) chosen.Add(application);
            }

            if (chosen.Count > GroupValidator.MaxShortcutsPerGroup)
            {
                MessageBox.Show(this,
                    "A group holds at most " + GroupValidator.MaxShortcutsPerGroup + " shortcuts, and " +
                    chosen.Count + " are selected. Narrow the selection, or use a smart group rule instead.",
                    "Discovery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var prompt = new Form())
            {
                Text = "New group";
                ClientSize = new Size(420, 140);
                BackColor = ControlsBuilder.Surface;
                ForeColor = ControlsBuilder.Foreground;
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;

                var label = new Label
                {
                    Text = "Group name",
                    ForeColor = ControlsBuilder.Muted,
                    Location = new Point(16, 20),
                    AutoSize = true
                };

                var name = ControlsBuilder.Text("Applications", false, 380);
                name.Location = new Point(16, 42);

                Button create = ControlsBuilder.Button("Create", (s, e) => prompt.DialogResult = DialogResult.OK, 96, primary: true);
                create.Location = new Point(300, 88);

                prompt.Controls.Add(label);
                prompt.Controls.Add(name);
                prompt.Controls.Add(create);
                prompt.AcceptButton = create;

                if (prompt.ShowDialog(this) != DialogResult.OK) return;

                string groupName = (name.Text ?? string.Empty).Trim();
                if (groupName.Length == 0) return;

                ValidationResult check = GroupNameValidator.Validate(groupName);
                if (!check.IsValid)
                {
                    MessageBox.Show(this, check.Message, "Discovery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Group group = _services.CreateGroup(groupName);

                var items = new List<ShortcutItem>();
                int order = 0;

                foreach (DiscoveredApplication application in chosen)
                {
                    ResolvedShortcut resolved = _services.Shortcuts.Resolve(application.Target, application.DisplayName);

                    items.Add(new ShortcutItem
                    {
                        GroupId = group.Id,
                        Name = application.DisplayName,
                        Type = resolved.Type != ShortcutType.Unknown ? resolved.Type : application.Type,
                        Target = resolved.Target,
                        WorkingDirectory = resolved.SuggestedWorkingDirectory,
                        IconSource = application.IconHint ?? string.Empty,
                        Enabled = true,
                        SortOrder = order++,
                        IsAutoDetected = true
                    });
                }

                _services.GroupRepository.ReplaceShortcuts(group.Id, items);

                _services.Taskbar.CreateGroupShortcut(group.Name, _services.ExecutablePath, string.Empty, out _);

                MessageBox.Show(this,
                    "\"" + group.Name + "\" was created with " + items.Count + " shortcut(s).",
                    "Discovery", MessageBoxButtons.OK, MessageBoxIcon.Information);

                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void CreatePreset_Click(object sender, EventArgs e)
        {
            if (!(cboPreset.SelectedItem is PresetEntry preset))
            {
                MessageBox.Show(this, "Choose a rule first.", "Smart group",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_all.Count == 0)
            {
                MessageBox.Show(this, "Run a scan first, so the rule has something to match against.",
                    "Smart group", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var prompt = new Form())
            {
                Text = "Smart group";
                ClientSize = new Size(440, 190);
                BackColor = ControlsBuilder.Surface;
                ForeColor = ControlsBuilder.Foreground;
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;

                var nameLabel = new Label { Text = "Group name", ForeColor = ControlsBuilder.Muted, Location = new Point(16, 20), AutoSize = true };
                var name = ControlsBuilder.Text(preset.Rule.Name, false, 400);
                name.Location = new Point(16, 42);

                var modeLabel = new Label { Text = "Behaviour", ForeColor = ControlsBuilder.Muted, Location = new Point(16, 80), AutoSize = true };
                var mode = ControlsBuilder.Dropdown(240);
                mode.Items.Add(new ModeEntry(SmartGroupMode.Automatic, "Automatic: rebuild from the rule every time"));
                mode.Items.Add(new ModeEntry(SmartGroupMode.RuleBased, "Rule-based: add what matches, keep what I remove"));
                mode.Items.Add(new ModeEntry(SmartGroupMode.Manual, "Manual: create it once, then edit it by hand"));
                mode.Location = new Point(16, 102);
                mode.SelectedIndex = preset.Rule.Mode == SmartGroupMode.Automatic ? 0 : 1;

                var notes = new Label
                {
                    Text = DescribeRule(preset.Rule),
                    ForeColor = ControlsBuilder.Muted,
                    Location = new Point(16, 132),
                    Size = new Size(400, 40)
                };

                Button create = ControlsBuilder.Button("Create", (s, e) => prompt.DialogResult = DialogResult.OK, 96, primary: true);
                create.Location = new Point(320, 138);

                prompt.Controls.Add(nameLabel);
                prompt.Controls.Add(name);
                prompt.Controls.Add(modeLabel);
                prompt.Controls.Add(mode);
                prompt.Controls.Add(notes);
                prompt.Controls.Add(create);
                prompt.AcceptButton = create;

                if (prompt.ShowDialog(this) != DialogResult.OK) return;

                string groupName = (name.Text ?? string.Empty).Trim();
                if (groupName.Length == 0) return;

                ValidationResult check = GroupNameValidator.Validate(groupName);
                if (!check.IsValid)
                {
                    MessageBox.Show(this, check.Message, "Smart group", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                SmartGroupRule rule = preset.Rule;
                rule.Name = groupName;
                if (mode.SelectedItem is ModeEntry entry) rule.Mode = entry.Mode;

                var notes2 = new List<string>();
                Group group = _services.SmartGroups.CreateFromRule(rule, _all, notes2);

                _services.Taskbar.CreateGroupShortcut(group.Name, _services.ExecutablePath, string.Empty, out _);

                List<ShortcutItem> shortcuts = _services.GroupRepository.GetShortcuts(group.Id);

                MessageBox.Show(this,
                    "\"" + group.Name + "\" was created with " + shortcuts.Count + " shortcut(s)." +
                    (notes2.Count > 0 ? "\n\n" + string.Join("\n", notes2) : ""),
                    "Smart group", MessageBoxButtons.OK, MessageBoxIcon.Information);

                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private static string DescribeRule(SmartGroupRule rule)
        {
            if (rule.Categories != null && rule.Categories.Count > 0)
                return "Matches: " + string.Join(", ", rule.Categories) + ".";

            if (rule.Types != null && rule.Types.Count > 0)
                return "Matches: " + string.Join(", ", rule.Types) + ".";

            if (rule.Keywords != null && rule.Keywords.Count > 0)
                return "Matches names containing: " + string.Join(", ", rule.Keywords.Take(4)) + ".";

            return "Matches everything that was found.";
        }

        private void Close_Click(object sender, EventArgs e)
        {
            _scan?.Cancel();
            Close();
        }


        private sealed class CategoryEntry
        {
            public CategoryEntry(AppCategory? category, string label)
            {
                Category = category;
                Label = label;
            }

            public AppCategory? Category { get; }

            public string Label { get; }

            public override string ToString() => Label;
        }

        private sealed class PresetEntry
        {
            public PresetEntry(SmartGroupRule rule)
            {
                Rule = rule;
            }

            public SmartGroupRule Rule { get; }

            public override string ToString() => Rule.Name;
        }

        private sealed class ModeEntry
        {
            public ModeEntry(SmartGroupMode mode, string label)
            {
                Mode = mode;
                Label = label;
            }

            public SmartGroupMode Mode { get; }

            public string Label { get; }

            public override string ToString() => Label;
        }
    }
}