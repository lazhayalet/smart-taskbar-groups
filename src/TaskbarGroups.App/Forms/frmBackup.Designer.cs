using System;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGroups.App.Views;

namespace TaskbarGroups.App.Forms
{
    partial class frmBackup
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel rootLayout;
        private Label lblSummary;
        private ListView lstBackups;
        private FlowLayoutPanel actionBar;
        private Button btnCreate;
        private Button btnRestore;
        private Button btnExportJson;
        private Button btnImportJson;
        private Button btnOpenFolder;
        private Button btnClose;

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.rootLayout = new TableLayoutPanel();
            this.lblSummary = new Label();
            this.lstBackups = new ListView();
            this.actionBar = new FlowLayoutPanel();

            this.rootLayout.Dock = DockStyle.Fill;
            this.rootLayout.Padding = new Padding(12);
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.rootLayout.RowCount = 3;
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));

            this.lblSummary.Dock = DockStyle.Fill;
            this.lblSummary.ForeColor = ControlsBuilder.Muted;
            this.lblSummary.Font = new Font("Segoe UI", 9f);
            this.lblSummary.Text =
                "A backup contains the database, a JSON export of every group, workspace and setting, and the icon cache.\r\n" +
                "Restoring replaces what is here now. Your current data is always copied aside first.";

            this.lstBackups.Dock = DockStyle.Fill;
            this.lstBackups.View = View.Details;
            this.lstBackups.FullRowSelect = true;
            this.lstBackups.GridLines = true;
            this.lstBackups.HideSelection = false;
            this.lstBackups.BackColor = ControlsBuilder.Panel;
            this.lstBackups.ForeColor = ControlsBuilder.Foreground;
            this.lstBackups.Font = new Font("Segoe UI", 9f);
            this.lstBackups.BorderStyle = BorderStyle.FixedSingle;
            this.lstBackups.Columns.Add("Backup", 320);
            this.lstBackups.Columns.Add("Created", 160);
            this.lstBackups.Columns.Add("Size", 90);
            this.lstBackups.SelectedIndexChanged += new EventHandler((s, e) =>
                btnRestore.Enabled = lstBackups.SelectedItems.Count > 0);

            this.btnCreate = ControlsBuilder.Button("Back up now", this.Create_Click, 110, primary: true);
            this.btnRestore = ControlsBuilder.Button("Restore selected", this.Restore_Click, 110);
            this.btnRestore.Enabled = false;
            this.btnExportJson = ControlsBuilder.Button("Export JSON...", this.ExportJson_Click, 110);
            this.btnImportJson = ControlsBuilder.Button("Import JSON...", this.ImportJson_Click, 110);
            this.btnOpenFolder = ControlsBuilder.Button("Open folder", this.OpenFolder_Click, 110);
            this.btnClose = ControlsBuilder.Button("Close", this.Close_Click, 96);

            this.actionBar.Dock = DockStyle.Fill;
            this.actionBar.FlowDirection = FlowDirection.TopDown;
            this.actionBar.WrapContents = true;
            this.actionBar.Padding = new Padding(4, 2, 4, 2);

            var rowOne = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty
            };
            rowOne.Controls.Add(this.btnCreate);
            rowOne.Controls.Add(this.btnRestore);
            rowOne.Controls.Add(this.btnOpenFolder);

            var rowTwo = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty
            };
            rowTwo.Controls.Add(this.btnExportJson);
            rowTwo.Controls.Add(this.btnImportJson);
            rowTwo.Controls.Add(this.btnClose);

            this.actionBar.Controls.Add(rowOne);
            this.actionBar.Controls.Add(rowTwo);

            this.rootLayout.Controls.Add(this.lblSummary, 0, 0);
            this.rootLayout.Controls.Add(this.lstBackups, 0, 1);
            this.rootLayout.Controls.Add(this.actionBar, 0, 2);

            ControlsBuilder.StyleForm(this, "Taskbar Groups backup and restore", 720, 560);
            this.Controls.Add(this.rootLayout);
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