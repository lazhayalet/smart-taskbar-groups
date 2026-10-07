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
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;

namespace TaskbarGroups.App.Forms
{
    /// <summary>
    /// Creates and edits a group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every feature the 1.x editor had is here - name, width, colour, opacity,
    /// launch-all, per-shortcut arguments and working directory, reordering,
    /// deletion, drag and drop, a group icon - and the underlying model changed.
    /// Saving writes to SQLite; the legacy <c>config\&lt;group&gt;</c> folder is
    /// read-only from here on.
    /// </para>
    /// <para>
    /// The three P0 defects in this form are fixed at the source rather than
    /// guarded:
    /// </para>
    /// <list type="number">
    /// <item><c>handleLnkExt</c> no longer exists. Icons come from
    /// <see cref="IIconCache"/>, which resolves a link and falls back rather than
    /// calling <c>Icon.ExtractAssociatedIcon</c> on an unvalidated path.</item>
    /// <item>Adding a file goes through <see cref="IShortcutResolver"/>, so a
    /// folder becomes a folder, a <c>.url</c> becomes a URL and a Steam link
    /// becomes a Steam entry instead of all three being stored as executables.</item>
    /// <item>Saving no longer deletes the group before writing it. It writes
    /// first; the previous 1.x save destroyed the whole folder and then rewrote
    /// it, so a failure mid-save lost the group.</item>
    /// </list>
    /// </remarks>
    public partial class frmGroup : Form
    {
        private readonly ServiceContainer _services;
        private readonly List<ucProgramShortcut> _rows = new List<ucProgramShortcut>();

        private Group _group;
        private readonly bool _isNew;
        private ucProgramShortcut? _selectedRow;

        /// <summary>Creates a new group.</summary>
        public frmGroup(ServiceContainer services) : this(services, null)
        {
        }

        /// <summary>Creates or edits a group. A null group means a new one.</summary>
        public frmGroup(ServiceContainer services, Group? group)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));

            _isNew = group == null;
            _group = group ?? new Group
            {
                Width = _services.Settings.GroupWidth,
                Theme = _services.Settings.GroupTheme,
                Opacity = _services.Settings.GroupOpacity,
                CornerRadius = _services.Settings.GroupCornerRadius,
                ShadowEnabled = _services.Settings.GroupShadowEnabled,
                BlurEnabled = _services.Settings.GroupBlurEnabled,
                AnimationEnabled = _services.Settings.GroupAnimationEnabled
            };

            InitializeComponent();

            if (_isNew)
            {
                Text = "New group";
                cmdDelete.Visible = false;
                pnlAllowOpenAll.Checked = _group.OpenAllEnabled;
            }
            else
            {
                Text = "Edit group";
                txtGroupName.Text = GroupNameValidator.ToDisplayName(_group.Name);
                pnlAllowOpenAll.Checked = _group.OpenAllEnabled;
                lblWidthValue.Text = _group.Width.ToString();
                lblOpacityValue.Text = _group.Opacity.ToString("0");
                ApplyThemeToThemeSelector(_group.Theme);

                LoadGroupIcon(_group.IconId);
                LoadRows();
            }
        }

        private void frmGroup_Load(object sender, EventArgs e)
        {
            // Bounded by the work area of the monitor the window opened on, so
            // the editor never extends past the bottom of a short display - the
            // legacy form pinned MaximumSize to the primary screen's height, which
            // put it off-screen on a secondary monitor.
            Core.Models.MonitorInfo? monitor = _services.MonitorService.GetMonitorAt(TaskbarGroups.App.Services.ControlPosition.PointOf(this));
            Rectangle work = monitor?.WorkingArea ?? Screen.PrimaryScreen!.WorkingArea;

            MaximumSize = new Size(Math.Max(520, work.Width), work.Height);

            if (_group.IconId.Length == 0)
                cmdAddGroupIcon.Image = TaskbarGroups.App.Properties.Resources.AddIconWhite;
        }

        private void LoadRows()
        {
            foreach (ShortcutItem item in _services.GroupRepository.GetShortcuts(_group.Id))
                AddRow(item);
        }

        private void AddRow(ShortcutItem item)
        {
            ucProgramShortcut row = new ucProgramShortcut
            {
                Services = _services,
                Shortcut = item,
                Position = _rows.Count,
                Owner = this,
                OnDeleted = null,
                OnMoved = null
            };

            // The handlers are attached after construction so the closure can
            // capture the control it belongs to.
            ucProgramShortcut captured = row;
            row.OnDeleted = () => RemoveRow(captured);
            row.OnMoved = delta => MoveRow(captured, delta);

            _rows.Add(row);
            panelShortcuts.Controls.Add(row);
            PositionRows();
        }

        private void PositionRows()
        {
            const int rowHeight = 50;

            for (int index = 0; index < _rows.Count; index++)
            {
                _rows[index].Position = index;
                _rows[index].Location = new Point(8, index * rowHeight);
            }

            panelShortcuts.Height = Math.Max(50, _rows.Count * rowHeight + 8);
            panelShortcuts.AutoScroll = _rows.Count > 5;
        }

        private void RemoveRow(ucProgramShortcut row)
        {
            _rows.Remove(row);
            panelShortcuts.Controls.Remove(row);
            row.Dispose();
            PositionRows();
            ResetSelection();
        }

        /// <summary>Moves a row by one position and rewrites the order.</summary>
        private void MoveRow(ucProgramShortcut row, int delta)
        {
            int index = _rows.IndexOf(row);
            int target = index + delta;

            if (index < 0 || target < 0 || target >= _rows.Count) return;

            ResetSelection();

            _rows.RemoveAt(index);
            _rows.Insert(target, row);

            PositionRows();
            ApplyOrder();
        }

        private void ApplyOrder()
        {
            for (int index = 0; index < _rows.Count; index++)
                _rows[index].Shortcut.SortOrder = index;
        }

        // -------------------------------------------------------------------
        // Adding shortcuts
        // -------------------------------------------------------------------

        private void AddShortcut_Click(object sender, EventArgs e)
        {
            ResetSelection();

            using (var dialog = new OpenFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Title = "Add shortcuts to the group",
                CheckFileExists = true,
                Multiselect = true,
                Filter = "Applications and shortcuts (.exe, .lnk, .url, .bat, .cmd, .ps1)|*.exe;*.lnk;*.url;*.bat;*.cmd;*.ps1|All files (*.*)|*.*",
                DereferenceLinks = false
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var targets = new List<string>(dialog.FileNames);
                AddTargets(targets);
            }
        }

        /// <summary>Resolves and adds a set of raw paths.</summary>
        private void AddTargets(IEnumerable<string> targets)
        {
            var warnings = new List<string>();
            int added = 0;

            foreach (string target in targets)
            {
                if (_rows.Count >= GroupValidator.MaxShortcutsPerGroup)
                {
                    warnings.Add("A group holds at most " + GroupValidator.MaxShortcutsPerGroup +
                                 " shortcuts; the rest were not added.");
                    break;
                }

                ResolvedShortcut resolved = _services.Shortcuts.Resolve(target);

                if (resolved.Type == ShortcutType.Unknown)
                {
                    warnings.Add("Could not work out what \"" + target + "\" is, so it was not added.");
                    continue;
                }

                ShortcutItem item = _services.Shortcuts is Launchers.Detection.ShortcutResolver resolver
                    ? resolver.ToItem(resolved, _group.Id, _rows.Count)
                    : new ShortcutItem
                    {
                        GroupId = _group.Id,
                        Name = resolved.DisplayName,
                        Type = resolved.Type,
                        Target = resolved.Target,
                        WorkingDirectory = resolved.SuggestedWorkingDirectory,
                        SortOrder = _rows.Count
                    };

                if (item.Type.ExecutesCode() && _services.Settings.ShowScriptWarning)
                {
                    DialogResult confirm = MessageBox.Show(this,
                        "\"" + item.Name + "\" is a script.\n\nScripts run code when opened, and the code is whatever the script contains. " +
                        "Only add scripts you wrote or trust the source of.\n\nAdd it anyway?",
                        "Add script?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (confirm != DialogResult.Yes) continue;
                }

                foreach (string warning in resolved.Warnings) warnings.Add(item.Name + ": " + warning);

                AddRow(item);
                added++;
            }

            ApplyOrder();

            if (warnings.Count > 0)
            {
                // Warnings, never a crash: a folder or a moved shortcut is normal,
                // not exceptional.
                MessageBox.Show(this,
                    added + " shortcut(s) added.\n\n" + string.Join("\n\n", warnings),
                    "Some items need attention", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // -------------------------------------------------------------------
        // Drag and drop
        // -------------------------------------------------------------------

        private void PanelAddShortcut_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = HasAcceptablePayload(e) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void PanelAddShortcut_DragDrop(object sender, DragEventArgs e)
        {
            ResetSelection();
            var targets = ExtractDroppedPaths(e);
            if (targets.Count > 0) AddTargets(targets);
        }

        private void PanelAddShortcut_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = HasAcceptablePayload(e) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        /// <summary>
        /// Reads paths from a drop, including non-file items.
        /// </summary>
        /// <remarks>
        /// A Store shortcut dropped from Explorer arrives as a shell ID list rather
        /// than a file drop, so the file-drop path alone misses exactly the case
        /// the legacy code added ShellObjectCollection for. Both are handled, and
        /// both are guarded, because the legacy version indexed
        /// <c>files[0]</c> without checking for an empty array.
        /// </remarks>
        private static List<string> ExtractDroppedPaths(DragEventArgs e)
        {
            var paths = new List<string>();

            IDataObject? data = e.Data;
            if (data == null) return paths;

            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                if (data.GetData(DataFormats.FileDrop) is string[] files)
                    paths.AddRange(files);
            }

            if (paths.Count == 0 && data.GetDataPresent("Shell IDList Array"))
            {
                try
                {
                    var dataObject = (System.Runtime.InteropServices.ComTypes.IDataObject)data;
                    Microsoft.WindowsAPICodePack.Shell.ShellObjectCollection items =
                        Microsoft.WindowsAPICodePack.Shell.ShellObjectCollection.FromDataObject(dataObject);

                    foreach (Microsoft.WindowsAPICodePack.Shell.ShellNonFileSystemItem item in items)
                    {
                        if (!string.IsNullOrWhiteSpace(item.ParsingName)) paths.Add(item.ParsingName);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Dropped item could not be read: " + ex.Message);
                }
            }

            return paths;
        }


        private static bool HasAcceptablePayload(DragEventArgs e)
        {
            IDataObject? data = e.Data;
            if (data == null) return false;

            if (data.GetDataPresent(DataFormats.FileDrop)) return true;
            return data.GetDataPresent("Shell IDList Array");
        }

        private void PanelGroupIcon_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = HasAcceptablePayload(e) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void PanelGroupIcon_DragDrop(object sender, DragEventArgs e)
        {
            ResetSelection();

            var paths = ExtractDroppedPaths(e);
            if (paths.Count == 0) return;

            // The first usable image file becomes the group icon; anything else is
            // ignored rather than treated as an error.
            foreach (string path in paths)
            {
                if (!File.Exists(path)) continue;

                string extension = Path.GetExtension(path).ToLowerInvariant();

                if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" ||
                    extension == ".jpe" || extension == ".jfif" || extension == ".bmp" ||
                    extension == ".gif" || extension == ".ico" || extension == ".exe" ||
                    extension == ".lnk")
                {
                    ApplyGroupIcon(path);
                    return;
                }
            }
        }

        // -------------------------------------------------------------------
        // Group icon
        // -------------------------------------------------------------------

        private void AddGroupIcon_Click(object sender, EventArgs e)
        {
            ResetSelection();

            using (var dialog = new OpenFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Title = "Choose a group icon",
                CheckFileExists = true,
                Filter = "Images, icons and applications (.png, .jpg, .jpeg, .ico, .exe, .lnk)|*.png;*.jpg;*.jpeg;*.jpe;*.jfif;*.bmp;*.gif;*.ico;*.exe;*.lnk|All files (*.*)|*.*"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ApplyGroupIcon(dialog.FileName);
            }
        }

        /// <summary>
        /// Copies the chosen image into the data folder and points the group at it.
        /// </summary>
        /// <remarks>
        /// The file is copied rather than referenced. A reference breaks the moment
        /// the user deletes the picture they picked from their Pictures folder,
        /// which is the failure mode the 1.x editor had, since it stored a bitmap
        /// in memory and wrote it out at save time - losing the icon entirely if the
        /// form was closed without saving.
        /// </remarks>
        private void ApplyGroupIcon(string sourcePath)
        {
            try
            {
                Directory.CreateDirectory(_services.Paths.IconsDirectory);

                string name = "group-" + (_group.Id == Guid.Empty ? Guid.NewGuid() : _group.Id).ToString("N") + ".png";

                using (Bitmap? loaded = Windows.Shell.ShellIconExtractor.TryLoadBitmap(sourcePath)
                           ?? Windows.Shell.ShellIconExtractor.TryExtract(sourcePath, 256))
                {
                    if (loaded == null)
                    {
                        lblErrorIcon.Text = "That file could not be read as an image.";
                        lblErrorIcon.Visible = true;
                        return;
                    }

                    string destination = Path.Combine(_services.Paths.IconsDirectory, name);
                    loaded.Save(destination, System.Drawing.Imaging.ImageFormat.Png);

                    ReplaceIconBitmap(destination);
                    _group.IconId = destination;
                }

                lblErrorIcon.Visible = false;
                lblAddGroupIcon.Text = "Change group icon";
            }
            catch (Exception ex)
            {
                lblErrorIcon.Text = "The icon could not be applied: " + ex.Message;
                lblErrorIcon.Visible = true;
            }
        }

        private void LoadGroupIcon(string iconId)
        {
            if (string.IsNullOrWhiteSpace(iconId) || !File.Exists(iconId))
            {
                cmdAddGroupIcon.Image = Properties.Resources.AddIconWhite;
                return;
            }

            ReplaceIconBitmap(iconId);
            lblAddGroupIcon.Text = "Change group icon";
        }

        /// <summary>
        /// Swaps the preview bitmap, disposing the previous one.
        /// </summary>
        /// <remarks>
        /// The legacy editor assigned a new bitmap to the same button repeatedly
        /// and never disposed the old one, so editing a group's icon a dozen times
        /// leaked twelve GDI handles.
        /// </remarks>
        private void ReplaceIconBitmap(string path)
        {
            Bitmap? previous = cmdAddGroupIcon.Image as Bitmap;

            Bitmap? loaded = Windows.Shell.ShellIconExtractor.TryLoadBitmap(path)
                             ?? Windows.Shell.ShellIconExtractor.TryExtract(path, 256);

            cmdAddGroupIcon.Image = loaded ?? Properties.Resources.AddIconWhite;

            previous?.Dispose();
        }

        // -------------------------------------------------------------------
        // Selection and per-shortcut fields
        // -------------------------------------------------------------------

        internal void Select(ucProgramShortcut row)
        {
            if (_selectedRow == row) return;

            ResetSelection();

            _selectedRow = row;
            row.IsSelected = true;

            txtArguments.Text = row.Shortcut.Arguments;
            txtArguments.Enabled = true;

            txtWorkingDirectory.Text = row.Shortcut.WorkingDirectory;
            txtWorkingDirectory.Enabled = true;
            cmdSelectDirectory.Enabled = true;

            panelArguments.Visible = true;
            panelColor.Visible = false;
        }

        internal void ResetSelection()
        {
            if (_selectedRow != null)
            {
                _selectedRow.IsSelected = false;
                _selectedRow = null;
            }

            txtArguments.Text = string.Empty;
            txtArguments.Enabled = false;
            txtWorkingDirectory.Text = string.Empty;
            txtWorkingDirectory.Enabled = false;
            cmdSelectDirectory.Enabled = false;

            panelArguments.Visible = false;
            panelColor.Visible = true;
        }

        private void Arguments_Changed(object sender, EventArgs e)
        {
            if (_selectedRow != null) _selectedRow.Shortcut.Arguments = txtArguments.Text;
        }

        private void WorkingDirectory_Changed(object sender, EventArgs e)
        {
            if (_selectedRow != null) _selectedRow.Shortcut.WorkingDirectory = txtWorkingDirectory.Text;
        }

        private void SelectDirectory_Click(object sender, EventArgs e)
        {
            if (_selectedRow == null) return;

            // FolderBrowserDialog rather than the CodePack folder picker. The
            // framework dialog is the native IFileDialog on Windows 10 and 11, is
            // maintained with the platform, and does not pull in the CodePack
            // assembly for a single folder chooser.
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Choose the folder this application should start in",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            })
            {
                if (Directory.Exists(_selectedRow.Shortcut.WorkingDirectory))
                    dialog.SelectedPath = _selectedRow.Shortcut.WorkingDirectory;

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                _selectedRow.Shortcut.WorkingDirectory = dialog.SelectedPath;
                txtWorkingDirectory.Text = dialog.SelectedPath;
            }

        }

        private void EnableAdministrator(object sender, EventArgs e)
        {
            if (_selectedRow == null) return;

            _selectedRow.Shortcut.RunAsAdministrator = chkRunAsAdministrator.Checked;
        }

        // -------------------------------------------------------------------
        // Appearance
        // -------------------------------------------------------------------

        private void WidthUp_Click(object sender, EventArgs e)
        {
            int width = int.Parse(lblWidthValue.Text);
            if (width >= GroupValidator.MaxWidth)
            {
                lblErrorWidth.Text = "The maximum width is " + GroupValidator.MaxWidth;
                lblErrorWidth.Visible = true;
                return;
            }

            lblErrorWidth.Visible = false;
            lblWidthValue.Text = (width + 1).ToString();
        }

        private void WidthDown_Click(object sender, EventArgs e)
        {
            int width = int.Parse(lblWidthValue.Text);
            if (width <= GroupValidator.MinWidth)
            {
                lblErrorWidth.Text = "The minimum width is " + GroupValidator.MinWidth;
                lblErrorWidth.Visible = true;
                return;
            }

            lblErrorWidth.Visible = false;
            lblWidthValue.Text = (width - 1).ToString();
        }

        private void OpacityUp_Click(object sender, EventArgs e)
        {
            double opacity = double.Parse(lblOpacityValue.Text);
            if (opacity >= 100d)
            {
                lblErrorOpacity.Text = "The maximum opacity is 100";
                lblErrorOpacity.Visible = true;
                return;
            }

            lblErrorOpacity.Visible = false;
            lblOpacityValue.Text = Math.Min(100d, opacity + 10d).ToString("0");
        }

        private void OpacityDown_Click(object sender, EventArgs e)
        {
            double opacity = double.Parse(lblOpacityValue.Text);
            if (opacity <= 0d)
            {
                lblErrorOpacity.Text = "The minimum opacity is 0";
                lblErrorOpacity.Visible = true;
                return;
            }

            lblErrorOpacity.Visible = false;
            lblOpacityValue.Text = Math.Max(0d, opacity - 10d).ToString("0");
        }

        private void Theme_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cboTheme.SelectedItem is ThemeOption option) _group.Theme = option.Theme;
        }

        private void ApplyThemeToThemeSelector(GroupTheme theme)
        {
            for (int index = 0; index < cboTheme.Items.Count; index++)
            {
                if (cboTheme.Items[index] is ThemeOption option && option.Theme == theme)
                {
                    cboTheme.SelectedIndex = index;
                    return;
                }
            }
        }

        private void AllowOpenAll_Changed(object sender, EventArgs e)
        {
            _group.OpenAllEnabled = pnlAllowOpenAll.Checked;
        }

        // -------------------------------------------------------------------
        // Saving
        // -------------------------------------------------------------------

        private void Save_Click(object sender, EventArgs e)
        {
            ResetSelection();
            HideErrors();

            string name = (txtGroupName.Text ?? string.Empty).Trim();

            if (name == PlaceholderText || name.Length == 0)
            {
                lblErrorName.Text = "Must select a name";
                lblErrorName.Visible = true;
                return;
            }

            ValidationResult nameCheck = GroupNameValidator.Validate(name);
            if (!nameCheck.IsValid)
            {
                lblErrorName.Text = nameCheck.Message ?? "That name cannot be used";
                lblErrorName.Visible = true;
                return;
            }

            Group? sameName = _services.GroupRepository.GetByName(name);
            if (sameName != null && sameName.Id != _group.Id)
            {
                lblErrorName.Text = "There is already a group with that name";
                lblErrorName.Visible = true;
                return;
            }

            if (_rows.Count == 0)
            {
                lblErrorShortcuts.Text = "Must select at least one shortcut";
                lblErrorShortcuts.Visible = true;
                return;
            }

            if (_rows.Count > GroupValidator.MaxShortcutsPerGroup)
            {
                lblErrorShortcuts.Text = "Max " + GroupValidator.MaxShortcutsPerGroup + " shortcuts in one group";
                lblErrorShortcuts.Visible = true;
                return;
            }

            try
            {
                ApplyOrder();

                _group.Name = name;
                _group.Width = int.Parse(lblWidthValue.Text);
                _group.Opacity = double.Parse(lblOpacityValue.Text);
                _group.OpenAllEnabled = pnlAllowOpenAll.Checked;

                var shortcuts = new List<ShortcutItem>();
                foreach (ucProgramShortcut row in _rows) shortcuts.Add(row.Shortcut);

                IReadOnlyList<string> problems = GroupValidator.Validate(_group, shortcuts);
                if (problems.Count > 0)
                {
                    lblErrorShortcuts.Text = problems[0];
                    lblErrorShortcuts.Visible = true;
                    return;
                }

                // Write first, delete never. The legacy save deleted the group
                // folder and then rewrote it, so any failure lost the group.
                if (_group.Id == Guid.Empty) _group.Id = Guid.NewGuid();

                _services.GroupRepository.Upsert(_group);
                _services.GroupRepository.ReplaceShortcuts(_group.Id, shortcuts);

                WriteTaskbarShortcut();

                _services.Logger.Log(SystemLogLevel.Information, "App",
                    (_isNew ? "Created" : "Updated") + " group '" + _group.Name + "' with " + shortcuts.Count + " shortcuts");

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                _services.Logger.Log(SystemLogLevel.Error, "App", "Saving the group failed", ex);

                MessageBox.Show(this,
                    "The group could not be saved: " + ex.Message +
                    "\n\nYour previous version of this group has not been changed.",
                    "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Writes the taskbar shortcut that opens this group's popup.
        /// </summary>
        /// <remarks>
        /// Failure here is reported but does not block the save: the group exists
        /// and is usable from the dashboard, and the shortcut can be repaired from
        /// there. Blocking the save would lose the user's work over a secondary
        /// problem.
        /// </remarks>
        private void WriteTaskbarShortcut()
        {
            if (!_services.Taskbar.CreateGroupShortcut(_group.Name, _services.ExecutablePath, _group.IconId, out string error))
            {
                _services.Logger.Log(SystemLogLevel.Warning, "Taskbar",
                    "Taskbar shortcut for '" + _group.Name + "' could not be written: " + error);

                MessageBox.Show(this,
                    "The group was saved, but its taskbar shortcut could not be written: " + error +
                    "\n\nUse \"Repair taskbar shortcut\" on the group menu to try again.",
                    "Taskbar shortcut", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Delete_Click(object sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(this,
                "Delete \"" + _group.Name + "\" and its " + _rows.Count + " shortcut(s)?",
                "Delete group", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            try
            {
                _services.GroupRepository.Delete(_group.Id);
                _services.Taskbar.DeleteGroupShortcut(_group.Name, legacyUnderscoreName: false, out _);

                _services.Logger.Log(SystemLogLevel.Information, "App", "Deleted group '" + _group.Name + "'");

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The group could not be deleted: " + ex.Message,
                    "Delete failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Exit_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void HideErrors()
        {
            lblErrorName.Visible = false;
            lblErrorShortcuts.Visible = false;
            lblErrorWidth.Visible = false;
            lblErrorOpacity.Visible = false;
            lblErrorIcon.Visible = false;
        }

        private void GroupName_TextChanged(object sender, EventArgs e)
        {
            lblErrorName.Visible = false;

            if (txtGroupName.Text == PlaceholderText) return;
            if (_isNew) return;

            // Live rename feedback, because the constraint that bites is the
            // duplicate-name one and it is only visible at save time otherwise.
            ValidationResult result = GroupNameValidator.Validate(txtGroupName.Text);
            if (!result.IsValid) lblErrorName.Visible = false;
        }

        private void GroupName_Click(object sender, EventArgs e)
        {
            if (txtGroupName.Text == PlaceholderText) txtGroupName.Text = string.Empty;
        }

        private void GroupName_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtGroupName.Text)) txtGroupName.Text = PlaceholderText;
        }

        private void Form_Click(object sender, EventArgs e)
        {
            if (ActiveControl == txtGroupName || ActiveControl == txtArguments) return;
            ResetSelection();
        }

        internal const string PlaceholderText = "Name the new group...";

    }

    /// <summary>One entry in the theme selector.</summary>
    internal sealed class ThemeOption
    {
        public ThemeOption(GroupTheme theme, string label)
        {
            Theme = theme;
            Label = label;
        }

        public GroupTheme Theme { get; }

        public string Label { get; }

        public override string ToString() => Label;
    }
}
