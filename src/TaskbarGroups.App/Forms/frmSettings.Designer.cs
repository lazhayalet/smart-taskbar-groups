using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarGroups.App.Forms
{
    partial class frmSettings
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel rootLayout;
        private TabControl tabs;
        private TabPage tabGeneral;
        private TabPage tabLaunchers;
        private TabPage tabIcons;
        private TabPage tabBackup;
        private TabPage tabAdvanced;
        private FlowLayoutPanel buttonBar;

        private CheckBox chkStartWithWindows;
        private CheckBox chkStartMinimized;
        private CheckBox chkStartInTray;
        private CheckBox chkCheckUpdates;
        private CheckBox chkOfflineMode;
        private Label lblOfflineNote;

        private CheckBox chkConfirmOpenAll;
        private NumericUpDown numOpenAllThreshold;
        private CheckBox chkEnableScripts;
        private CheckBox chkScriptWarning;
        private CheckBox chkDefaultAdmin;
        private Label lblAdminNote;

        private CheckBox chkIconCache;
        private NumericUpDown numIconSize;
        private CheckBox chkRemoteFavicons;
        private CheckBox chkExplorerMenu;

        private CheckBox chkAutoBackup;
        private NumericUpDown numBackupInterval;
        private NumericUpDown numBackupRotation;
        private Label lblBackupLocation;

        private CheckBox chkGlobalHotkeys;
        private TextBox txtHotkey;
        private Button cmdResetHotkey;
        private NumericUpDown numPopupOffset;
        private NumericUpDown numPopupGap;
        private CheckBox chkLogging;
        private CheckBox chkVerboseLogging;
        private Label lblLogsLocation;
        private ComboBox cboLanguage;

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.rootLayout = new TableLayoutPanel();
            this.tabs = new TabControl();
            this.tabGeneral = new TabPage("General");
            this.tabLaunchers = new TabPage("Launchers");
            this.tabIcons = new TabPage("Icons and integration");
            this.tabBackup = new TabPage("Backup and updates");
            this.tabAdvanced = new TabPage("Advanced");
            this.buttonBar = new FlowLayoutPanel();

            this.chkStartWithWindows = new CheckBox();
            this.chkStartMinimized = new CheckBox();
            this.chkStartInTray = new CheckBox();
            this.chkCheckUpdates = new CheckBox();
            this.chkOfflineMode = new CheckBox();
            this.lblOfflineNote = new Label();

            this.chkConfirmOpenAll = new CheckBox();
            this.numOpenAllThreshold = new NumericUpDown();
            this.chkEnableScripts = new CheckBox();
            this.chkScriptWarning = new CheckBox();
            this.chkDefaultAdmin = new CheckBox();
            this.lblAdminNote = new Label();

            this.chkIconCache = new CheckBox();
            this.numIconSize = new NumericUpDown();
            this.chkRemoteFavicons = new CheckBox();
            this.chkExplorerMenu = new CheckBox();

            this.chkAutoBackup = new CheckBox();
            this.numBackupInterval = new NumericUpDown();
            this.numBackupRotation = new NumericUpDown();
            this.lblBackupLocation = new Label();

            this.chkGlobalHotkeys = new CheckBox();
            this.txtHotkey = new TextBox();
            this.cmdResetHotkey = new Button();
            this.numPopupOffset = new NumericUpDown();
            this.numPopupGap = new NumericUpDown();
            this.chkLogging = new CheckBox();
            this.cboLanguage = new ComboBox();
            this.chkVerboseLogging = new CheckBox();
            this.lblLogsLocation = new Label();

            this.SuspendLayout();

            StyleCheck(this.chkStartWithWindows, "Start with Windows");
            StyleCheck(this.chkStartMinimized, "Start minimised");
            StyleCheck(this.chkStartInTray, "Start in the notification area");
            StyleCheck(this.chkCheckUpdates, "Check for updates at start-up");
            StyleCheck(this.chkOfflineMode, "Offline mode");

            this.lblOfflineNote = Views.ControlsBuilder.Body(
                "Offline mode stops every network request. Groups, workspaces, icons and backups keep working; " +
                "only the update check and remote favicons need the network.",
                muted: true);
            this.lblOfflineNote.MaximumSize = new Size(520, 0);

            StyleCheck(this.chkConfirmOpenAll, "Confirm before opening a large group");
            this.numOpenAllThreshold.Minimum = 1;
            this.numOpenAllThreshold.Maximum = 99;
            this.numOpenAllThreshold.Width = 70;
            StyleNumeric(this.numOpenAllThreshold);

            StyleCheck(this.chkEnableScripts, "Allow .bat, .cmd and .ps1 shortcuts");
            StyleCheck(this.chkScriptWarning, "Warn before adding a script");
            StyleCheck(this.chkDefaultAdmin, "Default new shortcuts to run as administrator");

            this.lblAdminNote = Views.ControlsBuilder.Body(
                "Windows asks for permission every time an elevated shortcut is opened, and Taskbar Groups never " +
                "elevates anything on its own. A shortcut only runs elevated when you tick this for that shortcut.",
                muted: true);
            this.lblAdminNote.MaximumSize = new Size(520, 0);

            StyleCheck(this.chkIconCache, "Cache icons on disk");
            this.numIconSize.Minimum = 16;
            this.numIconSize.Maximum = 128;
            this.numIconSize.Width = 70;
            StyleNumeric(this.numIconSize);

            StyleCheck(this.chkRemoteFavicons, "Allow remote favicons for web addresses");
            StyleCheck(this.chkExplorerMenu, "Add \"Add to Taskbar Group\" to the Explorer context menu");

            StyleCheck(this.chkAutoBackup, "Back up automatically");
            this.numBackupInterval.Minimum = 1;
            this.numBackupInterval.Maximum = 168;
            this.numBackupInterval.Width = 70;
            StyleNumeric(this.numBackupInterval);

            this.numBackupRotation.Minimum = 1;
            this.numBackupRotation.Maximum = 100;
            this.numBackupRotation.Width = 70;
            StyleNumeric(this.numBackupRotation);

            StyleCheck(this.chkGlobalHotkeys, "Enable the global shortcut");
            this.txtHotkey.BackColor = Views.ControlsBuilder.Raised;
            this.txtHotkey.ForeColor = Views.ControlsBuilder.Foreground;
            this.txtHotkey.Width = 160;
            this.txtHotkey.BorderStyle = BorderStyle.FixedSingle;

            this.cmdResetHotkey = SmallButton("Reset");
            this.cmdResetHotkey.Click += new EventHandler(this.ResetHotkey_Click);

            this.numPopupOffset.Minimum = 0;
            this.numPopupOffset.Maximum = 100;
            this.numPopupOffset.Width = 70;
            StyleNumeric(this.numPopupOffset);

            this.numPopupGap.Minimum = 0;
            this.numPopupGap.Maximum = 100;
            this.numPopupGap.Width = 70;
            StyleNumeric(this.numPopupGap);

            StyleCheck(this.chkLogging, "Write log files");
            StyleCheck(this.chkVerboseLogging, "Verbose logging (larger files)");

            BuildTab(this.tabGeneral, new Control[]
            {
                Views.ControlsBuilder.Heading("Language"),
                Row("Application language", this.cboLanguage),
                Views.ControlsBuilder.Heading("Startup"),
                this.chkStartWithWindows,
                this.chkStartMinimized,
                this.chkStartInTray,
                Views.ControlsBuilder.Heading("Updates"),
                this.chkCheckUpdates,
                this.chkOfflineMode,
                this.lblOfflineNote
            });

            BuildTab(this.tabLaunchers, new Control[]
            {
                Views.ControlsBuilder.Heading("Opening many at once"),
                this.chkConfirmOpenAll,
                Row("Ask when a group has at least", this.numOpenAllThreshold, "shortcuts"),
                Views.ControlsBuilder.Heading("Scripts"),
                this.chkEnableScripts,
                this.chkScriptWarning,
                Views.ControlsBuilder.Heading("Administrator"),
                this.chkDefaultAdmin,
                this.lblAdminNote
            });

            BuildTab(this.tabIcons, new Control[]
            {
                Views.ControlsBuilder.Heading("Icons"),
                this.chkIconCache,
                Row("Preferred size", this.numIconSize, "pixels"),
                this.chkRemoteFavicons,
                Views.ControlsBuilder.Heading("Windows integration"),
                this.chkExplorerMenu,
                Views.ControlsBuilder.Body("The context menu is added for the current user only, and is removed again when this is switched off.", muted: true)
            });

            this.lblBackupLocation = Views.ControlsBuilder.Body("", muted: true);
            this.lblBackupLocation.MaximumSize = new Size(520, 0);

            BuildTab(this.tabBackup, new Control[]
            {
                Views.ControlsBuilder.Heading("Backup"),
                this.chkAutoBackup,
                Row("Every", this.numBackupInterval, "hours"),
                Row("Keep the most recent", this.numBackupRotation, "backups"),
                this.lblBackupLocation,
                Views.ControlsBuilder.Heading("Updates"),
                this.chkCheckUpdates,
                Views.ControlsBuilder.Body("Updates are only ever downloaded from the project's GitHub releases page. There is no paid service involved.", muted: true)
            });

            this.lblLogsLocation = Views.ControlsBuilder.Body("", muted: true);
            this.lblLogsLocation.MaximumSize = new Size(520, 0);

            BuildTab(this.tabAdvanced, new Control[]
            {
                Views.ControlsBuilder.Heading("Global shortcut"),
                this.chkGlobalHotkeys,
                Row("Shortcut", this.txtHotkey, this.cmdResetHotkey),
                Views.ControlsBuilder.Body("Format: Ctrl, Alt, Shift or Win, then a key. For example Ctrl+Alt+G.", muted: true),
                Views.ControlsBuilder.Heading("Popup placement"),
                Row("Gap from the screen edge", this.numPopupOffset, "pixels"),
                Row("Gap above the cursor", this.numPopupGap, "pixels"),
                Views.ControlsBuilder.Heading("Logging"),
                this.chkLogging,
                this.chkVerboseLogging,
                this.lblLogsLocation
            });

            this.tabs.Dock = DockStyle.Fill;
            this.tabs.Controls.Add(this.tabGeneral);
            this.tabs.Controls.Add(this.tabLaunchers);
            this.tabs.Controls.Add(this.tabIcons);
            this.tabs.Controls.Add(this.tabBackup);
            this.tabs.Controls.Add(this.tabAdvanced);
            StyleTabs(this.tabs);

            Button save = Views.ControlsBuilder.Button("Save", this.Save_Click, 96, primary: true);
            Button cancel = Views.ControlsBuilder.Button("Cancel", this.Cancel_Click, 96);

            this.buttonBar.Dock = DockStyle.Bottom;
            this.buttonBar.FlowDirection = FlowDirection.RightToLeft;
            this.buttonBar.Height = 52;
            this.buttonBar.Padding = new Padding(12, 8, 12, 8);
            this.buttonBar.WrapContents = false;
            this.buttonBar.Controls.Add(save);
            this.buttonBar.Controls.Add(cancel);

            this.rootLayout.Dock = DockStyle.Fill;
            this.rootLayout.Padding = new Padding(12);
            this.rootLayout.Controls.Add(this.tabs, 0, 0);

            Views.ControlsBuilder.StyleForm(this, "Taskbar Groups settings", 720, 620);
            this.Controls.Add(this.rootLayout);
            this.Controls.Add(this.buttonBar);

            this.tabGeneral.ResumeLayout(false);
            this.tabLaunchers.ResumeLayout(false);
            this.tabIcons.ResumeLayout(false);
            this.tabBackup.ResumeLayout(false);
            this.tabAdvanced.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        /// <summary>
        /// Fills a tab with a vertical stack of controls.
        /// </summary>
        /// <remarks>
        /// One flow layout per tab rather than a designer grid: the settings are a
        /// list of labelled rows, and the flow layout lets the DPI scale decide the
        /// row height without any hardcoded pixel values.
        /// </remarks>
        private static void BuildTab(TabPage page, Control[] children)
        {
            var host = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(8)
            };

            foreach (Control child in children)
            {
                if (child is Label label && label.AutoSize) label.MaximumSize = new Size(520, 0);
                host.Controls.Add(child);
            }

            page.Controls.Add(host);
            page.BackColor = Views.ControlsBuilder.Panel;
            page.ForeColor = Views.ControlsBuilder.Foreground;
            page.Padding = new Padding(8);
        }

        /// <summary>A "label, control, suffix" row.</summary>
        private static FlowLayoutPanel Row(string label, Control control, object trailing = null)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 2, 0, 2)
            };

            row.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                ForeColor = Views.ControlsBuilder.Foreground,
                Font = new Font("Segoe UI", 9.5f),
                Margin = new Padding(0, 6, 6, 0),
                Width = 190
            });

            control.Margin = new Padding(0, 2, 6, 2);
            row.Controls.Add(control);

            if (trailing is Control trailingControl)
            {
                trailingControl.Margin = new Padding(0, 6, 0, 0);
                row.Controls.Add(trailingControl);
            }
            else if (trailing is string suffix)
            {
                row.Controls.Add(new Label
                {
                    Text = suffix,
                    AutoSize = true,
                    ForeColor = Views.ControlsBuilder.Muted,
                    Font = new Font("Segoe UI", 9.5f),
                    Margin = new Padding(2, 6, 0, 0)
                });
            }

            return row;
        }

        private static void StyleCheck(CheckBox box, string text)
        {
            box.Text = text;
            box.AutoSize = true;
            box.ForeColor = Views.ControlsBuilder.Foreground;
            box.Font = new Font("Segoe UI", 9.5f);
            box.Margin = new Padding(0, 4, 0, 4);
        }

        private static void StyleNumeric(NumericUpDown number)
        {
            number.BackColor = Views.ControlsBuilder.Raised;
            number.ForeColor = Views.ControlsBuilder.Foreground;
        }

        private static Button SmallButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(80, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Views.ControlsBuilder.Raised,
                ForeColor = Views.ControlsBuilder.Foreground,
                UseVisualStyleBackColor = false,
                Margin = new Padding(4, 2, 4, 2)
            };

            button.FlatAppearance.BorderColor = Views.ControlsBuilder.Border;
            return button;
        }

        private static void StyleTabs(TabControl tabs)
        {
            tabs.Font = new Font("Segoe UI", 9.5f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
