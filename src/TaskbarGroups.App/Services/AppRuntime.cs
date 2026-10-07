using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TaskbarGroups.App.Forms;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Windows.Monitor;

namespace TaskbarGroups.App.Services
{
    /// <summary>
    /// Owns the process's windows and the tray icon.
    /// </summary>
    /// <remarks>
    /// A taskbar-group utility has two front ends - the dashboard and the group
    /// popups - and each popup is its own short-lived process. This type is what
    /// keeps the process alive correctly in each case: the dashboard keeps a
    /// message loop running with a tray icon as the exit path, and a popup runs a
    /// form that closes itself.
    ///
    /// Tray mode is opt-out and default-on when "start in tray" is set, because a
    /// utility whose only window is a borderless popup has to be reachable from
    /// somewhere.
    /// </remarks>
    public sealed class AppRuntime
    {
        private readonly ServiceContainer _services;
        private readonly List<frmMain> _popups = new List<frmMain>();

        private NotifyIcon? _tray;
        private frmClient? _dashboard;
        private ApplicationContext? _context;
        private bool _shuttingDown;

        private AppRuntime(ServiceContainer services)
        {
            _services = services;
        }

        public static AppRuntime Create()
        {
            return new AppRuntime(ServiceContainer.Create());
        }

        public ServiceContainer Services => _services;

        /// <summary>
        /// Opens the dashboard and, when configured, hides it behind the tray icon.
        /// </summary>
        public void StartDashboard()
        {
            _dashboard = new frmClient(_services);
            _context = new TrayApplicationContext(this, _dashboard);

            if (!_services.Settings.StartMinimized && !_services.Settings.StartInTray)
            {
                _dashboard.Show();
            }
            else
            {
                // Created but not shown: the tray icon is the visible presence
                // until the user opens it.
                _dashboard.CreateControl();
            }

            EnsureTrayIcon();

            Application.Run(_context);
        }

        /// <summary>
        /// Opens one group popup. The process exits when the popup closes, which is
        /// what keeps the taskbar from accumulating hidden processes.
        /// </summary>
        public void StartGroupPopup(string groupName)
        {
            Point invocation = MonitorService.GetCursorPosition();

            var popup = new frmMain(_services, groupName, invocation);
            _popups.Add(popup);

            try
            {
                Application.Run(popup);
            }
            finally
            {
                _popups.Remove(popup);
                popup.Dispose();
            }
        }

        /// <summary>Brings the dashboard forward, creating it if it was closed.</summary>
        public void ShowDashboard()
        {
            if (_dashboard == null || _dashboard.IsDisposed)
            {
                _dashboard = new frmClient(_services);
            }

            if (_dashboard.Visible) _dashboard.Activate();
            else _dashboard.Show();

            if (_dashboard.WindowState == FormWindowState.Minimized)
                _dashboard.WindowState = FormWindowState.Normal;

            _dashboard.BringToFront();
            _dashboard.Focus();
        }

        public void HideDashboard()
        {
            if (_dashboard != null && !_dashboard.IsDisposed && _dashboard.Visible)
                _dashboard.Hide();
        }

        /// <summary>Closes every popup this process opened.</summary>
        public void ClosePopups()
        {
            foreach (frmMain popup in _popups.ToList())
            {
                try
                {
                    if (!popup.IsDisposed) popup.Close();
                }
                catch (Exception)
                {
                }
            }
        }

        public void EnsureTrayIcon()
        {
            if (_tray != null) return;

            _tray = new NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "Taskbar Groups",
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip()
            };

            _tray.DoubleClick += (s, e) => ShowDashboard();

            BuildTrayMenu(_tray.ContextMenuStrip!);
        }

        private void BuildTrayMenu(ContextMenuStrip menu)
        {
            menu.Items.Clear();

            menu.Items.Add(Item(TaskbarGroups.App.Configuration.Localization.T("Open Taskbar Groups"), ShowDashboard));

            var groups = new ToolStripMenuItem(TaskbarGroups.App.Configuration.Localization.T("Groups"));
            foreach (Group group in _services.GroupRepository.GetAll())
            {
                Group captured = group;
                groups.DropDownItems.Add(Item(group.Name, () => LaunchGroup(captured)));
            }

            if (groups.DropDownItems.Count == 0)
                groups.DropDownItems.Add(new ToolStripMenuItem("(no groups yet)") { Enabled = false });

            menu.Items.Add(groups);

            var workspaces = new ToolStripMenuItem(TaskbarGroups.App.Configuration.Localization.T("Workspaces"));
            foreach (Workspace workspace in _services.WorkspaceRepository.GetAll())
            {
                Workspace captured = workspace;
                workspaces.DropDownItems.Add(Item(workspace.Name, () => _ = LaunchWorkspaceAsync(captured)));
            }

            if (workspaces.DropDownItems.Count == 0)
                workspaces.DropDownItems.Add(new ToolStripMenuItem("(no workspaces yet)") { Enabled = false });

            menu.Items.Add(workspaces);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Settings", () => ShowSettings()));
            menu.Items.Add(Item("Diagnostics", () => ShowDiagnostics()));
            menu.Items.Add(Item("Backup now", () => CreateBackup()));
            menu.Items.Add(Item("Check for updates", () => CheckForUpdates()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Restart", Restart));
            menu.Items.Add(Item("Exit", Shutdown));
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

        /// <summary>Opens a group's popup in this process rather than starting another.</summary>
        private void LaunchGroup(Group group)
        {
            try
            {
                Point invocation = MonitorService.GetCursorPosition();

                var popup = new frmMain(_services,
                    Core.Validation.GroupNameValidator.ToStorageName(group.Name),
                    invocation);

                _popups.Add(popup);
                popup.FormClosed += (s, e) =>
                {
                    _popups.Remove(popup);
                    popup.Dispose();
                };

                popup.Show();
            }
            catch (Exception ex)
            {
                _services.Logger.Log(SystemLogLevel.Error, "App", "Group popup failed", ex);
                MessageBox.Show(ex.Message, "Taskbar Groups", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async System.Threading.Tasks.Task LaunchWorkspaceAsync(Workspace workspace)
        {
            try
            {
                var service = new TaskbarGroups.Workspaces.WorkspaceService.WorkspaceService(
                    _services.WorkspaceRepository,
                    _services.Launchers,
                    _services.Windows,
                    _services.MonitorService,
                    _services.CreateLaunchContext(),
                    _services.Logger);

                WorkspaceLaunchResult result = await service.LaunchWorkspaceAsync(workspace);

                if (!result.Success && !string.IsNullOrEmpty(result.Message))
                {
                    MessageBox.Show(result.Message, workspace.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                _services.Logger.Log(SystemLogLevel.Error, "App", "Workspace launch failed", ex);
                MessageBox.Show(ex.Message, workspace.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowSettings()
        {
            ShowDashboard();
            _dashboard?.ShowSettingsTab();
        }

        private void ShowDiagnostics()
        {
            ShowDashboard();
            _dashboard?.ShowDiagnosticsTab();
        }

        private void CreateBackup()
        {
            try
            {
                string path = _services.Backups.CreateBackup("tray");
                MessageBox.Show("Backup written to:\n" + path, "Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("The backup failed: " + ex.Message, "Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void CheckForUpdates()
        {
            try
            {
                UpdateCheckResult result = await _services.Updates.CheckAsync();

                if (!result.CheckSucceeded)
                {
                    // Offline is the normal case for a local utility, so this is
                    // information rather than an error.
                    MessageBox.Show(result.Message, "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!result.UpdateAvailable)
                {
                    MessageBox.Show("You are up to date.", "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                DialogResult answer = MessageBox.Show(
                    "Version " + result.LatestVersion + " is available.\n\nOpen the releases page?",
                    "Update available", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

                if (answer == DialogResult.OK && !string.IsNullOrEmpty(result.ReleaseUrl))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = result.ReleaseUrl,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("The update check failed: " + ex.Message, "Check for updates",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Restart()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _services.ExecutablePath,
                    WorkingDirectory = Path.GetDirectoryName(_services.ExecutablePath) ?? string.Empty,
                    UseShellExecute = true
                });

                Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show("The application could not be restarted: " + ex.Message,
                    "Restart", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void Shutdown()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;

            try
            {
                // Take a backup on the way out unless the user has switched that
                // off, because exiting is the most likely moment for unsaved work
                // to exist.
                if (_services.Settings.AutomaticBackupEnabled)
                {
                    try
                    {
                        _services.Backups.CreateBackup("shutdown");
                        _services.Backups.PruneOldBackups(_services.Settings.BackupRotationCount);
                    }
                    catch (Exception ex)
                    {
                        _services.Logger.Log(SystemLogLevel.Warning, "Backup", "Shutdown backup failed", ex);
                    }
                }
            }
            catch (Exception)
            {
            }

            try { _tray?.Dispose(); } catch (Exception) { }
            _tray = null;

            ClosePopups();

            _context?.ExitThread();
        }

        private Icon LoadTrayIcon()
        {
            try
            {
                // The application icon when it is embedded, so a portable copy
                // does not need a separate .ico beside the exe.
                Icon? embedded = System.Drawing.Icon.ExtractAssociatedIcon(_services.ExecutablePath);
                if (embedded != null) return embedded;
            }
            catch (Exception)
            {
            }

            return SystemIcons.Application;
        }
    }

    /// <summary>
    /// Keeps a message loop running with no visible window, so the process stays
    /// alive when the dashboard is hidden behind the tray icon.
    /// </summary>
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly AppRuntime _runtime;
        private readonly frmClient _dashboard;

        public TrayApplicationContext(AppRuntime runtime, frmClient dashboard)
        {
            _runtime = runtime;
            _dashboard = dashboard;

            // The dashboard's real close is a hide, because closing it must not
            // leave the user with no way back to the application.
            _dashboard.FormClosing += OnDashboardClosing;

            _dashboard.Shown += (s, e) =>
            {
                if (runtime.Services.Settings.StartMinimized || runtime.Services.Settings.StartInTray)
                    _dashboard.Hide();
            };
        }

        private void OnDashboardClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !_runtime.Services.Settings.StartInTray)
            {
                _runtime.Shutdown();
                e.Cancel = true;
                return;
            }

            // Otherwise the window is hidden and the tray icon remains.
            e.Cancel = true;
            _dashboard.Hide();
        }

        protected override void ExitThreadCore()
        {
            _dashboard.FormClosing -= OnDashboardClosing;
            base.ExitThreadCore();
        }
    }
}