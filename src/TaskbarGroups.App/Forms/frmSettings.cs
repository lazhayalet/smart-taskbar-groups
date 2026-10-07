using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGroups.App.Views;
using TaskbarGroups.App.Services;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Models;

namespace TaskbarGroups.App.Forms
{
    /// <summary>
    /// Settings.
    /// </summary>
    /// <remarks>
    /// Every setting is written through <see cref="ISettingsService"/>-equivalent
    /// storage immediately, and the ones with a side effect outside the database -
    /// start with Windows, Explorer integration - apply on save rather than as the
    /// control is touched, so a cancelled dialog leaves nothing behind.
    /// </remarks>
    public partial class frmSettings : Form
    {
        private readonly ServiceContainer _services;
        private readonly AppSettings _working;

        public frmSettings(ServiceContainer services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));

            // Edited on a copy, so Cancel really cancels.
            _working = services.Settings.Clone();

            InitializeComponent();

            LoadValues();
        }

        private void LoadValues()
        {
            chkStartWithWindows.Checked = _services.Startup.IsEnabled();
            chkStartMinimized.Checked = _working.StartMinimized;
            chkStartInTray.Checked = _working.StartInTray;

            chkConfirmOpenAll.Checked = _working.ConfirmBeforeOpenAll;
            numOpenAllThreshold.Value = Clamp(_working.OpenAllWarningThreshold, 1, 99);

            chkEnableScripts.Checked = _working.EnableScriptLaunching;
            chkScriptWarning.Checked = _working.ShowScriptWarning;
            chkDefaultAdmin.Checked = _working.DefaultRunAsAdministrator;

            chkIconCache.Checked = _working.IconCacheEnabled;
            numIconSize.Value = Clamp(_working.PreferredIconSize, 16, 128);
            chkRemoteFavicons.Checked = _working.AllowRemoteFavicons;

            chkExplorerMenu.Checked = _working.ExplorerContextMenuEnabled;

            chkAutoBackup.Checked = _working.AutomaticBackupEnabled;
            numBackupInterval.Value = Clamp(_working.AutomaticBackupIntervalHours, 1, 168);
            numBackupRotation.Value = Clamp(_working.BackupRotationCount, 1, 100);

            chkCheckUpdates.Checked = _working.CheckForUpdatesOnStartup;
            chkOfflineMode.Checked = _working.OfflineMode;

            chkGlobalHotkeys.Checked = _working.EnableGlobalHotkeys;
            txtHotkey.Text = _working.ShowGroupHotkey;

            chkLogging.Checked = _working.LoggingEnabled;
            chkVerboseLogging.Checked = _working.VerboseLogging;

            cboLanguage.Items.Clear();
            cboLanguage.Items.AddRange(new object[] { "English", "Türkçe" });
            cboLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
            cboLanguage.SelectedIndex = string.Equals(_working.Language, "tr", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            numPopupOffset.Value = Clamp(_working.GroupPopupOffset, 0, 100);
            numPopupGap.Value = Clamp(_working.GroupPopupGap, 0, 100);
        }

        private static decimal Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void Save_Click(object sender, EventArgs e)
        {
            try
            {
                _working.StartMinimized = chkStartMinimized.Checked;
                _working.StartInTray = chkStartInTray.Checked;
                _working.ConfirmBeforeOpenAll = chkConfirmOpenAll.Checked;
                _working.OpenAllWarningThreshold = (int)numOpenAllThreshold.Value;
                _working.EnableScriptLaunching = chkEnableScripts.Checked;
                _working.ShowScriptWarning = chkScriptWarning.Checked;
                _working.DefaultRunAsAdministrator = chkDefaultAdmin.Checked;
                _working.IconCacheEnabled = chkIconCache.Checked;
                _working.PreferredIconSize = (int)numIconSize.Value;
                _working.AllowRemoteFavicons = chkRemoteFavicons.Checked;
                _working.AutomaticBackupEnabled = chkAutoBackup.Checked;
                _working.AutomaticBackupIntervalHours = (int)numBackupInterval.Value;
                _working.BackupRotationCount = (int)numBackupRotation.Value;
                _working.CheckForUpdatesOnStartup = chkCheckUpdates.Checked;
                _working.OfflineMode = chkOfflineMode.Checked;
                _working.EnableGlobalHotkeys = chkGlobalHotkeys.Checked;
                _working.LoggingEnabled = chkLogging.Checked;
                _working.VerboseLogging = chkVerboseLogging.Checked;
                _working.GroupPopupOffset = (int)numPopupOffset.Value;
                _working.GroupPopupGap = (int)numPopupGap.Value;

                // Language: stored in settings, applied to open forms and the
                // tray immediately so the change is visible without a restart.
                _working.Language = cboLanguage.SelectedIndex == 1 ? "tr" : "en";
                TaskbarGroups.App.Configuration.Localization.Current = _working.Language;
                foreach (Form openForm in Application.OpenForms)
                    TaskbarGroups.App.Configuration.Localization.ApplyTo(openForm);

                // The hotkey is validated before it is stored, so a bad value is
                // reported here rather than silently disabling the shortcut.
                string? hotkey = HotkeyGesture.Normalize(txtHotkey.Text);
                if (hotkey == null)
                {
                    MessageBox.Show(this,
                        "\"" + txtHotkey.Text + "\" is not a usable shortcut. Use a form like Ctrl+Alt+G.",
                        "Shortcut", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtHotkey.Focus();
                    return;
                }

                string? conflict = HotkeyGesture.FindConflict(hotkey, new[] { HotkeyGesture.Defaults.OpenAll });
                if (conflict != null)
                {
                    MessageBox.Show(this,
                        hotkey + " clashes with " + conflict + ", which the group popup already uses.",
                        "Shortcut", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtHotkey.Focus();
                    return;
                }

                _working.ShowGroupHotkey = hotkey;

                ApplyStartupSetting();
                ApplyExplorerSetting();

                _services.SaveSettings(_working);

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                _services.Logger.Log(Core.Interfaces.SystemLogLevel.Error, "App", "Saving settings failed", ex);
                MessageBox.Show(this, "The settings could not be saved: " + ex.Message,
                    "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Applies "start with Windows" by adding or removing the per-user Run
        /// entry. Only the application's own value is touched.
        /// </summary>
        private void ApplyStartupSetting()
        {
            bool wanted = chkStartWithWindows.Checked;
            bool current = _services.Startup.IsEnabled();

            if (wanted == current) return;

            if (wanted)
            {
                if (!_services.Startup.Enable(out string error))
                {
                    MessageBox.Show(this,
                        "Taskbar Groups could not be registered to start with Windows: " + error,
                        "Startup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    chkStartWithWindows.Checked = false;
                    return;
                }
            }
            else
            {
                _services.Startup.Disable(out _);
            }

            _working.StartWithWindows = wanted;
        }

        private void ApplyExplorerSetting()
        {
            try
            {
                var integration = new Windows.ExplorerIntegration.ExplorerContextMenuService(_services.ExecutablePath);

                if (chkExplorerMenu.Checked)
                {
                    if (!integration.Enable(out string error))
                    {
                        MessageBox.Show(this,
                            "The Explorer context menu could not be enabled: " + error,
                            "Explorer integration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        chkExplorerMenu.Checked = false;
                        return;
                    }
                }
                else
                {
                    integration.Disable(out _);
                }
            }
            catch (Exception ex)
            {
                _services.Logger.Log(Core.Interfaces.SystemLogLevel.Warning, "App", "Explorer integration failed", ex);
            }
        }

        private void Cancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void ResetHotkey_Click(object sender, EventArgs e)
        {
            txtHotkey.Text = HotkeyGesture.Defaults.MainWindow;
        }
    }
}