using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TaskbarGroups.App.Services;
using TaskbarGroups.App.Views;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Windows.Monitor;
using TaskbarGroups.Workspaces.WorkspaceService;

namespace TaskbarGroups.App.Forms
{
    /// <summary>
    /// Workspace manager: build, launch and capture window arrangements.
    /// </summary>
    /// <remarks>
    /// Workspaces are the feature with the most honest limits, and the UI says so.
    /// Placing a window requires finding it after launch, which is a race the OS
    /// does not offer a clean answer to, so each item waits with a timeout and the
    /// result reports how many windows were arranged and how many were not. A
    /// workspace with one app that takes 15 seconds to show its window reports one
    /// window not found rather than claiming success.
    /// </remarks>
    public partial class frmWorkspaces : Form
    {
        private readonly ServiceContainer _services;
        private Workspace? _selected;

        public frmWorkspaces(ServiceContainer services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            InitializeComponent();

            Reload();
        }

        private WorkspaceService CreateService()
        {
            return new WorkspaceService(
                _services.WorkspaceRepository,
                _services.Launchers,
                _services.Windows,
                _services.MonitorService,
                _services.CreateLaunchContext(),
                _services.Logger)
            {
                PlacementTimeoutMs = _services.Settings.WorkspacePlacementTimeoutMs
            };
        }

        private void Reload()
        {
            lstWorkspaces.BeginUpdate();
            lstWorkspaces.Items.Clear();

            foreach (Workspace workspace in _services.WorkspaceRepository.GetAll())
            {
                var item = new ListViewItem(new[]
                {
                    workspace.Name,
                    workspace.Items.Count.ToString(),
                    workspace.LaunchDelayMs + " ms",
                    string.IsNullOrEmpty(workspace.Description) ? "" : workspace.Description
                });

                item.Tag = workspace;
                lstWorkspaces.Items.Add(item);
            }

            lstWorkspaces.EndUpdate();

            _selected = null;
            lstItems.Items.Clear();
            SetButtons();
        }

        private Workspace? Selected
        {
            get
            {
                if (lstWorkspaces.SelectedItems.Count == 0) return null;
                return lstWorkspaces.SelectedItems[0].Tag as Workspace;
            }
        }

        private void SetButtons()
        {
            bool hasSelection = _selected != null;

            btnLaunch.Enabled = hasSelection;
            btnDuplicate.Enabled = hasSelection;
            btnDelete.Enabled = hasSelection;
            btnCapture.Enabled = hasSelection;
            btnAddItem.Enabled = hasSelection;
            btnRemoveItem.Enabled = hasSelection;
        }

        private void lstWorkspaces_SelectedIndexChanged(object sender, EventArgs e)
        {
            _selected = Selected;
            LoadItems();
            SetButtons();
        }

        private void LoadItems()
        {
            lstItems.Items.Clear();

            if (_selected == null) return;

            string primary = _services.MonitorService.GetPrimary()?.DeviceName ?? "(unknown)";

            foreach (WorkspaceItem item in _selected.Items.OrderBy(i => i.SortOrder))
            {
                string executable = string.IsNullOrWhiteSpace(item.Executable)
                    ? "(attach to a running window)"
                    : Path.GetFileName(item.Executable);

                string monitor = string.IsNullOrWhiteSpace(item.MonitorDeviceName)
                    ? primary
                    : DescribeMonitor(item.MonitorDeviceName);

                var row = new ListViewItem(new[]
                {
                    item.ProcessMatch.Length > 0 ? item.ProcessMatch : executable,
                    executable,
                    monitor,
                    item.NormalizedPlacement.ToString(),
                    (item.Width > 0 ? item.Width.ToString() : "auto") + " x " + (item.Height > 0 ? item.Height.ToString() : "auto")
                });

                row.Tag = item;
                lstItems.Items.Add(row);
            }
        }

        /// <summary>
        /// Turns a device name into something readable.
        /// </summary>
        /// <remarks>
        /// A workspace stores the device name because it survives a reboot, but
        /// "\\.\DISPLAY2" means nothing to a user. When the named monitor is not
        /// currently connected that is worth saying too, since the item will be
        /// placed on the primary instead.
        /// </remarks>
        private string DescribeMonitor(string deviceName)
        {
            MonitorInfo? monitor = _services.MonitorService.GetMonitorByDeviceName(deviceName);

            if (monitor == null)
                return deviceName + " (not connected)";

            return monitor.Bounds.Width + "x" + monitor.Bounds.Height + " at " + monitor.Bounds.X + "," + monitor.Bounds.Y;
        }

        private void New_Click(object sender, EventArgs e)
        {
            using (var prompt = new Form())
            {
                Text = "New workspace";
                ClientSize = new Size(420, 140);
                BackColor = ControlsBuilder.Surface;
                ForeColor = ControlsBuilder.Foreground;
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;

                prompt.Controls.Add(new Label
                {
                    Text = "Workspace name",
                    ForeColor = ControlsBuilder.Muted,
                    Location = new Point(16, 20),
                    AutoSize = true
                });

                var name = ControlsBuilder.Text(string.Empty, false, 380);
                name.Location = new Point(16, 42);

                Button create = ControlsBuilder.Button("Create", (s, e2) => prompt.DialogResult = DialogResult.OK, 96, primary: true);
                create.Location = new Point(300, 88);

                prompt.Controls.Add(name);
                prompt.Controls.Add(create);
                prompt.AcceptButton = create;

                if (prompt.ShowDialog(this) != DialogResult.OK) return;

                string workspaceName = (name.Text ?? string.Empty).Trim();
                if (workspaceName.Length == 0) return;

                Workspace workspace = CreateService().CreateWorkspace(workspaceName);
                Reload();

                Select(workspace);
            }
        }

        private void Duplicate_Click(object sender, EventArgs e)
        {
            if (_selected == null) return;

            try
            {
                Workspace copy = CreateService().DuplicateWorkspace(_selected.Id);
                Reload();
                Select(copy);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Workspaces", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Delete_Click(object sender, EventArgs e)
        {
            if (_selected == null) return;

            if (MessageBox.Show(this,
                    "Delete \"" + _selected.Name + "\" and its " + _selected.Items.Count + " item(s)?",
                    "Delete workspace", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            CreateService().DeleteWorkspace(_selected.Id);
            Reload();
        }

        private void Select(Workspace workspace)
        {
            foreach (ListViewItem item in lstWorkspaces.Items)
            {
                if (item.Tag is Workspace candidate && candidate.Id == workspace.Id)
                {
                    item.Selected = true;
                    item.EnsureVisible();
                    break;
                }
            }
        }

        private async void Launch_Click(object sender, EventArgs e)
        {
            if (_selected == null) return;

            Workspace workspace = _selected;

            btnLaunch.Enabled = false;
            UseWaitCursor = true;
            lblStatus.Text = "Launching " + workspace.Name + "...";

            try
            {
                WorkspaceLaunchResult result = await CreateService().LaunchWorkspaceAsync(workspace);

                // The counts are the honest part of the result and are what is
                // shown: a workspace that opened three of four windows says so.
                lblStatus.Text = result.Success
                    ? "All " + result.WindowsPlaced + " window(s) arranged."
                    : result.WindowsPlaced + " window(s) arranged, " + result.WindowsNotFound + " could not be found.";

                if (!result.Success && result.LaunchResults.Any(r => !r.Success))
                {
                    string failures = string.Join("\n", result.LaunchResults
                        .Where(r => !r.Success)
                        .Select(r => "  " + r.ResolvedTarget + ": " + r.ErrorMessage));

                    MessageBox.Show(this,
                        "Some applications could not be started:\n\n" + failures +
                        "\n\nWindows that did open were still arranged.",
                        workspace.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                _services.Logger.Log(SystemLogLevel.Error, "Workspace", "Launch failed", ex);
                lblStatus.Text = "The launch failed: " + ex.Message;
            }
            finally
            {
                UseWaitCursor = false;
                SetButtons();
            }
        }

        private void Capture_Click(object sender, EventArgs e)
        {
            if (_selected == null) return;

            try
            {
                WorkspaceService service = CreateService();
                service.SaveCurrentLayout(_selected);

                int stored = _services.WorkspaceRepository.GetPlacements(_selected.Id).Count;

                lblStatus.Text = stored > 0
                    ? "Saved " + stored + " window position(s). They are reapplied the next time this workspace launches."
                    : "No positions were saved, because none of the open windows belong to this workspace.";

                if (stored == 0)
                {
                    MessageBox.Show(this,
                        "No windows matched.\n\nTo capture a layout, open the workspace first, arrange the windows as you want them, " +
                        "then press \"Save current layout\".\n\nOnly windows launched by this workspace can be captured.",
                        "Save current layout", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Workspaces", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void AddItem_Click(object sender, EventArgs e)
        {
            if (_selected == null) return;

            using (var dialog = new OpenFileDialog
            {
                Title = "Add an application to this workspace",
                CheckFileExists = true,
                Multiselect = true,
                Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                foreach (string file in dialog.FileNames)
                {
                    _selected.Items.Add(new WorkspaceItem
                    {
                        Executable = file,
                        ProcessMatch = Path.GetFileName(file),
                        MonitorDeviceName = _services.MonitorService.GetPrimary()?.DeviceName ?? string.Empty,
                        NormalizedPlacement = PlacementAnchor.Center,
                        SortOrder = _selected.Items.Count
                    });
                }
            }

            SaveAndRefresh();
        }

        private void RemoveItem_Click(object sender, EventArgs e)
        {
            if (_selected == null || lstItems.SelectedItems.Count == 0) return;

            foreach (ListViewItem row in lstItems.SelectedItems)
            {
                if (row.Tag is WorkspaceItem item) _selected.Items.Remove(item);
            }

            SaveAndRefresh();
        }

        private void EditItem_Click(object sender, EventArgs e)
        {
            if (_selected == null || lstItems.SelectedItems.Count == 0) return;
            if (!(lstItems.SelectedItems[0].Tag is WorkspaceItem item)) return;

            using (var prompt = new Form())
            {
                Text = "Window placement";
                ClientSize = new Size(460, 330);
                BackColor = ControlsBuilder.Surface;
                ForeColor = ControlsBuilder.Foreground;
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;

                var anchor = ControlsBuilder.Dropdown(260);
                foreach (PlacementAnchor value in Enum.GetValues(typeof(PlacementAnchor)))
                    anchor.Items.Add(new AnchorEntry(value, Humanize(value.ToString())));

                int current = FindAnchorIndex(anchor, item.NormalizedPlacement);
                anchor.SelectedIndex = current >= 0 ? current : 0;

                var monitor = ControlsBuilder.Dropdown(260);
                foreach (MonitorInfo info in _services.MonitorService.GetMonitors().Monitors)
                    monitor.Items.Add(new MonitorEntry(info.DeviceName,
                        info.Bounds.Width + "x" + info.Bounds.Height + " at " + info.Bounds.X + "," + info.Bounds.Y +
                        (info.Primary ? " (primary)" : "")));

                int monitorIndex = FindMonitorIndex(monitor, item.MonitorDeviceName);
                monitor.SelectedIndex = monitorIndex >= 0 ? monitorIndex : 0;

                var width = ControlsBuilder.Number(item.Width > 0 ? item.Width : 900, 0, 10000, 90);
                var height = ControlsBuilder.Number(item.Height > 0 ? item.Height : 700, 0, 10000, 90);
                var state = ControlsBuilder.Dropdown(140);
                foreach (WindowStatePreference value in Enum.GetValues(typeof(WindowStatePreference)))
                    state.Items.Add(new StateEntry(value, Humanize(value.ToString())));
                state.SelectedIndex = (int)item.WindowState;

                int y = 20;
                AddRow(prompt, "Position on the monitor", anchor, ref y);
                AddRow(prompt, "Monitor", monitor, ref y);
                AddRow(prompt, "Width (0 = keep the window's own)", width, ref y);
                AddRow(prompt, "Height (0 = keep the window's own)", height, ref y);
                AddRow(prompt, "Window state", state, ref y);

                Button save = ControlsBuilder.Button("Save", (s, e2) => prompt.DialogResult = DialogResult.OK, 96, primary: true);
                save.Location = new Point(344, y + 10);

                prompt.Controls.Add(save);
                prompt.AcceptButton = save;

                if (prompt.ShowDialog(this) != DialogResult.OK) return;

                if (anchor.SelectedItem is AnchorEntry chosenAnchor) item.NormalizedPlacement = chosenAnchor.Anchor;
                if (monitor.SelectedItem is MonitorEntry chosenMonitor) item.MonitorDeviceName = chosenMonitor.DeviceName;
                if (state.SelectedItem is StateEntry chosenState) item.WindowState = chosenState.State;

                item.Width = (int)width.Value;
                item.Height = (int)height.Value;

                // Explicit sizes and an anchor are mutually exclusive: the anchor
                // only applies when no size was given.
                if (item.Width > 0 || item.Height > 0)
                    item.NormalizedPlacement = PlacementAnchor.Center;

                SaveAndRefresh();
            }
        }

        /// <summary>
        /// Index of the anchor entry matching a stored value.
        /// </summary>
        /// <remarks>
        /// Scanned rather than found with a predicate because
        /// <c>ComboBox.ObjectCollection</c> has no LINQ-friendly lookup, and the
        /// items are typed wrappers rather than plain strings.
        /// </remarks>
        private static int FindAnchorIndex(ComboBox combo, PlacementAnchor anchor)
        {
            for (int index = 0; index < combo.Items.Count; index++)
            {
                if (combo.Items[index] is AnchorEntry entry && entry.Anchor == anchor) return index;
            }
            return -1;
        }

        private static int FindMonitorIndex(ComboBox combo, string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return -1;

            for (int index = 0; index < combo.Items.Count; index++)
            {
                if (combo.Items[index] is MonitorEntry entry &&
                    string.Equals(entry.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
            return -1;
        }

        private static void AddRow(Form form, string label, Control control, ref int y)
        {
            form.Controls.Add(new Label
            {
                Text = label,
                ForeColor = ControlsBuilder.Muted,
                Location = new Point(16, y + 6),
                Size = new Size(230, 20)
            });

            control.Location = new Point(252, y);
            form.Controls.Add(control);

            y += 38;
        }

        private static string Humanize(string value)
        {
            var builder = new System.Text.StringBuilder();
            for (int index = 0; index < value.Length; index++)
            {
                if (index > 0 && char.IsUpper(value[index])) builder.Append(' ');
                builder.Append(index == 0 ? value[index] : char.ToLowerInvariant(value[index]));
            }
            return builder.ToString();
        }

        private void SaveAndRefresh()
        {
            if (_selected == null) return;

            _services.WorkspaceRepository.Upsert(_selected);
            LoadItems();
        }

        private void Close_Click(object sender, EventArgs e)
        {
            Close();
        }

        private sealed class AnchorEntry
        {
            public AnchorEntry(PlacementAnchor anchor, string label)
            {
                Anchor = anchor;
                Label = label;
            }

            public PlacementAnchor Anchor { get; }

            public string Label { get; }

            public override string ToString() => Label;
        }

        private sealed class MonitorEntry
        {
            public MonitorEntry(string deviceName, string label)
            {
                DeviceName = deviceName;
                Label = label;
            }

            public string DeviceName { get; }

            public string Label { get; }

            public override string ToString() => Label;
        }

        private sealed class StateEntry
        {
            public StateEntry(WindowStatePreference state, string label)
            {
                State = state;
                Label = label;
            }

            public WindowStatePreference State { get; }

            public string Label { get; }

            public override string ToString() => Label;
        }
    }
}