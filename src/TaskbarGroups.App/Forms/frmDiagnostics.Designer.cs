using System;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGroups.App.Views;

namespace TaskbarGroups.App.Forms
{
    partial class frmDiagnostics
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel rootLayout;
        private Label lblSummary;
        private Label lblStatus;
        private TabControl tabControl;
        private TabPage tabChecks;
        private TabPage tabMonitors;
        private TabPage tabShortcuts;

        private ListView lstChecks;
        private ListView lstMonitors;
        private ListView lstShortcuts;

        private FlowLayoutPanel actionBar;
        private Button btnRefresh;
        private Button btnRepair;
        private Button btnIcons;
        private Button btnValidate;
        private Button btnExport;
        private Button btnLogs;
        private Button btnResetPosition;
        private Button btnClose;

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.rootLayout = new TableLayoutPanel();
            this.lblSummary = new Label();
            this.lblStatus = new Label();
            this.tabControl = new TabControl();
            this.tabChecks = new TabPage("Checks");
            this.tabMonitors = new TabPage("Monitors");
            this.tabShortcuts = new TabPage("Shortcuts");
            this.lstChecks = new ListView();
            this.lstMonitors = new ListView();
            this.lstShortcuts = new ListView();
            this.actionBar = new FlowLayoutPanel();
            this.btnRefresh = new Button();
            this.btnRepair = new Button();
            this.btnIcons = new Button();
            this.btnValidate = new Button();
            this.btnExport = new Button();
            this.btnLogs = new Button();
            this.btnResetPosition = new Button();
            this.btnClose = new Button();

            this.rootLayout.Dock = DockStyle.Fill;
            this.rootLayout.Padding = new Padding(12);
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.rootLayout.RowCount = 3;
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));

            this.lblSummary.Dock = DockStyle.Fill;
            this.lblSummary.ForeColor = ControlsBuilder.Foreground;
            this.lblSummary.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            this.lblSummary.TextAlign = ContentAlignment.MiddleLeft;

            this.lblStatus.Dock = DockStyle.Fill;
            this.lblStatus.ForeColor = ControlsBuilder.Muted;
            this.lblStatus.Font = new Font("Segoe UI", 9f);
            this.lblStatus.TextAlign = ContentAlignment.MiddleLeft;

            ConfigureList(this.lstChecks, new[] { "Check", "Status", "Detail" },
                new[] { 160, 80, 0 });
            ConfigureList(this.lstMonitors, new[] { "Device", "Resolution", "Position", "Scale", "Taskbar", "Primary" },
                new[] { 140, 100, 90, 60, 90, 70 });
            ConfigureList(this.lstShortcuts, new[] { "Shortcut", "Type", "Status", "Detail" },
                new[] { 180, 90, 110, 0 });

            this.tabChecks.Controls.Add(this.lstChecks);
            this.tabMonitors.Controls.Add(this.lstMonitors);
            this.tabShortcuts.Controls.Add(this.lstShortcuts);

            foreach (TabPage page in new[] { this.tabChecks, this.tabMonitors, this.tabShortcuts })
            {
                page.BackColor = ControlsBuilder.Panel;
                page.ForeColor = ControlsBuilder.Foreground;
                page.Padding = new Padding(6);
            }

            this.tabControl.Dock = DockStyle.Fill;
            this.tabControl.Font = new Font("Segoe UI", 9.5f);
            this.tabControl.Controls.Add(this.tabChecks);
            this.tabControl.Controls.Add(this.tabMonitors);
            this.tabControl.Controls.Add(this.tabShortcuts);

            this.btnRefresh = ControlsBuilder.Button("Re-run checks", this.Refresh_Click, 110);
            this.btnRepair = ControlsBuilder.Button("Repair database", this.RepairDatabase_Click, 110);
            this.btnIcons = ControlsBuilder.Button("Rebuild icon cache", this.RebuildIcons_Click, 110);
            this.btnValidate = ControlsBuilder.Button("Validate shortcuts", this.ValidateShortcuts_Click, 110);
            this.btnExport = ControlsBuilder.Button("Export report", this.Export_Click, 110);
            this.btnLogs = ControlsBuilder.Button("Open logs folder", this.OpenLogs_Click, 110);
            this.btnResetPosition = ControlsBuilder.Button("Reset popup position", this.ResetPosition_Click, 110);
            this.btnClose = ControlsBuilder.Button("Close", this.Close_Click, 96, primary: true);

            this.actionBar.Dock = DockStyle.Bottom;
            this.actionBar.FlowDirection = FlowDirection.LeftToRight;
            this.actionBar.WrapContents = true;
            this.actionBar.Height = 76;
            this.actionBar.Padding = new Padding(8, 4, 8, 4);

            this.actionBar.Controls.Add(this.btnRefresh);
            this.actionBar.Controls.Add(this.btnRepair);
            this.actionBar.Controls.Add(this.btnIcons);
            this.actionBar.Controls.Add(this.btnValidate);
            this.actionBar.Controls.Add(this.btnExport);
            this.actionBar.Controls.Add(this.btnLogs);
            this.actionBar.Controls.Add(this.btnResetPosition);
            this.actionBar.Controls.Add(this.btnClose);

            this.rootLayout.Controls.Add(this.lblSummary, 0, 0);
            this.rootLayout.Controls.Add(this.tabControl, 0, 1);
            this.rootLayout.Controls.Add(this.lblStatus, 0, 2);

            ControlsBuilder.StyleForm(this, "Taskbar Groups diagnostics", 880, 640);
            this.Controls.Add(this.rootLayout);
            this.Controls.Add(this.actionBar);
        }

        /// <summary>
        /// One list configuration helper.
        /// </summary>
        /// <remarks>
        /// Three detail lists need identical behaviour - full-row select, grid
        /// lines, a dark palette, a proportion column that fills the remainder.
        /// Repeating that per list would be six copies of the same setup.
        /// </remarks>
        private static void ConfigureList(ListView list, string[] columns, int[] widths)
        {
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.GridLines = true;
            list.HideSelection = false;
            list.BackColor = ControlsBuilder.Panel;
            list.ForeColor = ControlsBuilder.Foreground;
            list.Font = new Font("Segoe UI", 9f);
            list.BorderStyle = BorderStyle.FixedSingle;

            for (int index = 0; index < columns.Length; index++)
            {
                list.Columns.Add(columns[index], widths[index] < 0 ? widths.Length + widths[index] : widths[index]);

                if (widths[index] == 0)
                {
                    // A zero width means "take the remaining space", so the width is
                    // recalculated whenever the window is resized.
                    list.Columns[index].Width = 200;
                }
            }
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