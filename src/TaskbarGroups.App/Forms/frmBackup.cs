using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using TaskbarGroups.App.Services;
using TaskbarGroups.App.Views;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Data.Json;

namespace TaskbarGroups.App.Forms
{
    /// <summary>
    /// Backup, restore and the JSON import/export page.
    /// </summary>
    /// <remarks>
    /// One window for both jobs, because they are the same operation viewed from
    /// two directions: both produce a portable file describing every group,
    /// workspace and setting, and both accept one back.
    ///
    /// Restore is the dangerous half and is treated accordingly: the current
    /// database is copied aside first, the archive is validated before anything is
    /// touched, and every problem is reported rather than swallowed.
    /// </remarks>
    public partial class frmBackup : Form
    {
        private readonly ServiceContainer _services;

        public frmBackup(ServiceContainer services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            InitializeComponent();

            RefreshList();
        }

        private void RefreshList()
        {
            lstBackups.BeginUpdate();
            lstBackups.Items.Clear();

            try
            {
                if (Directory.Exists(_services.Paths.BackupsDirectory))
                {
                    FileInfo[] files = new DirectoryInfo(_services.Paths.BackupsDirectory)
                        .GetFiles("*.zip")
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .ToArray();

                    foreach (FileInfo file in files)
                    {
                        var item = new ListViewItem(new[]
                        {
                            file.Name,
                            file.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                            (file.Length / 1024) + " KB"
                        });

                        item.Tag = file.FullName;
                        lstBackups.Items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                _services.Logger.Log(SystemLogLevel.Warning, "Backup", "Could not list backups", ex);
            }
            finally
            {
                lstBackups.EndUpdate();
            }

            btnRestore.Enabled = lstBackups.Items.Count > 0;
        }

        private void Create_Click(object sender, EventArgs e)
        {
            try
            {
                string path = _services.Backups.CreateBackup("manual");

                _services.Backups.PruneOldBackups(_services.Settings.BackupRotationCount);
                RefreshList();

                MessageBox.Show(this,
                    "Backup written to:\n" + path +
                    "\n\nOld backups beyond the retention count were removed.",
                    "Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _services.Logger.Log(SystemLogLevel.Error, "Backup", "Backup failed", ex);
                MessageBox.Show(this, "The backup failed: " + ex.Message, "Backup",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void Restore_Click(object sender, EventArgs e)
        {
            if (lstBackups.SelectedItems.Count == 0) return;

            string path = lstBackups.SelectedItems[0].Tag as string ?? string.Empty;
            if (path.Length == 0) return;

            DialogResult confirm = MessageBox.Show(this,
                "Restore from:\n" + Path.GetFileName(path) + "\n\n" +
                "Every group, workspace and setting is replaced with the contents of this backup.\n" +
                "Your current data is copied aside first, so this can be undone.\n\nContinue?",
                "Restore backup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            btnRestore.Enabled = false;
            btnCreate.Enabled = false;
            UseWaitCursor = true;

            try
            {
                // Validate before touching anything: a corrupt archive should be
                // reported here, not half-applied.
                BackupValidationResult validation = _services.Backups.Validate(path);

                if (!validation.HasDatabase && !validation.HasJsonExport)
                {
                    MessageBox.Show(this,
                        "This archive cannot be restored:\n\n" + string.Join("\n", validation.Problems),
                        "Restore", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (validation.Problems.Count > 0)
                {
                    DialogResult proceed = MessageBox.Show(this,
                        "The archive is usable but has notes:\n\n" + string.Join("\n", validation.Problems) +
                        "\n\nContinue anyway?",
                        "Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (proceed != DialogResult.Yes) return;
                }

                RestoreResult result = await Task.Run(() => _services.Backups.Restore(path));

                if (!string.IsNullOrEmpty(result.PreviousDatabaseBackup))
                {
                    MessageBox.Show(this,
                        "The restore finished.\n\nYour previous data was saved to:\n" + result.PreviousDatabaseBackup,
                        "Restore", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this, "The restore finished.", "Restore",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                if (result.Problems.Count > 0)
                {
                    MessageBox.Show(this,
                        "Notes:\n\n" + string.Join("\n", result.Problems),
                        "Restore", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                RefreshList();
            }
            catch (Exception ex)
            {
                _services.Logger.Log(SystemLogLevel.Error, "Backup", "Restore failed", ex);
                MessageBox.Show(this, "The restore failed: " + ex.Message, "Restore",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
                btnCreate.Enabled = true;
                btnRestore.Enabled = lstBackups.Items.Count > 0;
            }
        }

        private void ExportJson_Click(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog
            {
                Title = "Export groups and workspaces",
                Filter = "Taskbar Groups export (*.json)|*.json|All files (*.*)|*.*",
                FileName = "taskbar-groups-export-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json",
                AddExtension = true,
                DefaultExt = "json"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    var service = new ExportService();
                    ExportDocument document = BuildExportDocument();
                    service.ExportFile(document, dialog.FileName);

                    MessageBox.Show(this,
                        "Exported " + document.Groups.Count + " group(s) and " +
                        document.Workspaces.Count + " workspace(s) to:\n" + dialog.FileName,
                        "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The export failed: " + ex.Message, "Export",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private ExportDocument BuildExportDocument()
        {
            var document = new ExportDocument
            {
                ApplicationVersion = ExportService.CurrentApplicationVersion(),
                Settings = _services.Settings
            };

            foreach (Group group in _services.GroupRepository.GetAll())
            {
                var exported = new ExportGroup
                {
                    Id = group.Id,
                    Name = group.Name,
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
                    SortMode = group.SortMode,
                    MonitorPreference = group.MonitorPreference,
                    PreferredMonitorDeviceName = group.PreferredMonitorDeviceName,
                    CreatedAt = group.CreatedAt,
                    UpdatedAt = group.UpdatedAt
                };

                foreach (ShortcutItem item in _services.GroupRepository.GetShortcuts(group.Id))
                {
                    exported.Shortcuts.Add(new ExportShortcut
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Type = item.Type,
                        Target = item.Target,
                        Arguments = item.Arguments,
                        WorkingDirectory = item.WorkingDirectory,
                        IconSource = item.IconSource,
                        RunAsAdministrator = item.RunAsAdministrator,
                        WindowState = item.WindowState,
                        PreferredMonitor = item.PreferredMonitor,
                        PreferredMonitorDeviceName = item.PreferredMonitorDeviceName,
                        Enabled = item.Enabled,
                        SortOrder = item.SortOrder,
                        CreatedAt = item.CreatedAt,
                        UpdatedAt = item.UpdatedAt
                    });
                }

                document.Groups.Add(exported);
            }

            foreach (Workspace workspace in _services.WorkspaceRepository.GetAll())
            {
                var exported = new ExportWorkspace
                {
                    Id = workspace.Id,
                    Name = workspace.Name,
                    Description = workspace.Description,
                    IconId = workspace.IconId,
                    LaunchDelayMs = workspace.LaunchDelayMs,
                    CreatedAt = workspace.CreatedAt,
                    UpdatedAt = workspace.UpdatedAt
                };

                foreach (WorkspaceItem item in workspace.Items)
                {
                    exported.Items.Add(new ExportWorkspaceItem
                    {
                        Id = item.Id,
                        ProcessMatch = item.ProcessMatch,
                        Executable = item.Executable,
                        MonitorDeviceName = item.MonitorDeviceName,
                        X = item.X,
                        Y = item.Y,
                        Width = item.Width,
                        Height = item.Height,
                        WindowState = item.WindowState,
                        NormalizedPlacement = item.NormalizedPlacement,
                        LaunchDelayMs = item.LaunchDelayMs,
                        SortOrder = item.SortOrder
                    });
                }

                exported.Placements.AddRange(_services.WorkspaceRepository.GetPlacements(workspace.Id));
                document.Workspaces.Add(exported);
            }

            document.Notes.Add("Icons are not embedded in an export; they are re-extracted from their sources.");
            return document;
        }

        private void ImportJson_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Import groups and workspaces",
                Filter = "Taskbar Groups export (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    var service = new ExportService();
                    ImportResult result = service.ImportFile(dialog.FileName);

                    if (!result.HasContent)
                    {
                        MessageBox.Show(this,
                            "Nothing was imported.\n\n" +
                            (result.Problems.Count > 0 ? string.Join("\n", result.Problems) : "The file contained no groups or workspaces."),
                            "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    DialogResult confirm = MessageBox.Show(this,
                        result.Groups.Count + " group(s) and " + result.Workspaces.Count + " workspace(s) were read.\n\n" +
                        (result.Groups.Count == 0
                            ? ""
                            : "A group whose name already exists is overwritten; the rest are added.\n\n") +
                        (result.Problems.Count > 0 || result.Notes.Count > 0
                            ? "Notes:\n" + string.Join("\n", result.Problems.Concat(result.Notes)) + "\n\n"
                            : "") +
                        "Import now?",
                        "Import", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                    if (confirm != DialogResult.Yes) return;

                    int importedGroups = 0;

                    foreach (Group group in result.Groups)
                    {
                        Group? existing = _services.GroupRepository.GetByName(group.Name);
                        if (existing != null) group.Id = existing.Id;

                        _services.GroupRepository.Upsert(group);

                        List<ShortcutItem> shortcuts = result.BuildShortcuts()
                            .Where(s => s.GroupId == group.Id)
                            .ToList();

                        _services.GroupRepository.ReplaceShortcuts(group.Id, shortcuts);
                        importedGroups++;
                    }

                    foreach (Workspace workspace in result.Workspaces)
                    {
                        Workspace? existing = _services.WorkspaceRepository.GetByName(workspace.Name);
                        if (existing != null) workspace.Id = existing.Id;
                        _services.WorkspaceRepository.Upsert(workspace);
                    }

                    MessageBox.Show(this,
                        importedGroups + " group(s) and " + result.Workspaces.Count + " workspace(s) imported.",
                        "Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The import failed: " + ex.Message, "Import",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void OpenFolder_Click(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_services.Paths.BackupsDirectory);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _services.Paths.BackupsDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Close_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}