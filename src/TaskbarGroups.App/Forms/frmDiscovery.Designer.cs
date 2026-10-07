using System;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGroups.App.Views;

namespace TaskbarGroups.App.Forms
{
    partial class frmDiscovery
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel rootLayout;
        private FlowLayoutPanel sourceBar;
        private CheckBox chkDesktop;
        private CheckBox chkProgramFiles;
        private CheckBox chkStoreApps;
        private CheckBox chkPwa;
        private CheckBox chkSteam;
        private Button btnScan;
        private Button btnCancel;

        private Panel filterBar;
        private TextBox txtFilter;
        private ComboBox cboCategory;

        private ListView lstResults;
        private Label lblProgress;

        private FlowLayoutPanel actionBar;
        private ComboBox cboPreset;
        private Button btnCreateGroup;
        private Button btnCreatePreset;
        private Button btnClose;

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.rootLayout = new TableLayoutPanel();
            this.sourceBar = new FlowLayoutPanel();
            this.filterBar = new Panel();
            this.lstResults = new ListView();
            this.lblProgress = new Label();
            this.actionBar = new FlowLayoutPanel();

            this.chkDesktop = SourceCheck("Desktop");
            this.chkProgramFiles = SourceCheck("Program Files");
            this.chkStoreApps = SourceCheck("Store apps", true);
            this.chkPwa = SourceCheck("Browser web apps", true);
            this.chkSteam = SourceCheck("Steam", false);

            this.btnScan = ControlsBuilder.Button("Scan", this.Scan_Click, 96, primary: true);
            this.btnCancel = ControlsBuilder.Button("Cancel", this.Cancel_Click, 96);
            this.btnCancel.Enabled = false;

            this.sourceBar.Dock = DockStyle.Fill;
            this.sourceBar.FlowDirection = FlowDirection.LeftToRight;
            this.sourceBar.WrapContents = false;
            this.sourceBar.Padding = new Padding(4, 6, 4, 2);

            this.sourceBar.Controls.Add(ControlsBuilder.Body("Sources:"));
            this.sourceBar.Controls.Add(this.chkDesktop);
            this.sourceBar.Controls.Add(this.chkProgramFiles);
            this.sourceBar.Controls.Add(this.chkStoreApps);
            this.sourceBar.Controls.Add(this.chkPwa);
            this.sourceBar.Controls.Add(this.chkSteam);
            this.sourceBar.Controls.Add(this.btnScan);
            this.sourceBar.Controls.Add(this.btnCancel);

            this.txtFilter = ControlsBuilder.Text(string.Empty, false, 240);
            this.txtFilter.PlaceholderText = "Filter results";
            this.txtFilter.Location = new Point(0, 6);
            this.txtFilter.TextChanged += new EventHandler(this.Filter_Changed);

            this.cboCategory = ControlsBuilder.Dropdown(200);
            this.cboCategory.Location = new Point(252, 4);
            this.cboCategory.SelectedIndexChanged += new EventHandler(this.Filter_Changed);

            this.filterBar.Dock = DockStyle.Fill;
            this.filterBar.Height = 40;
            this.filterBar.Controls.Add(this.txtFilter);
            this.filterBar.Controls.Add(this.cboCategory);

            this.lstResults.Dock = DockStyle.Fill;
            this.lstResults.View = View.Details;
            this.lstResults.FullRowSelect = true;
            this.lstResults.MultiSelect = true;
            this.lstResults.GridLines = true;
            this.lstResults.BackColor = ControlsBuilder.Panel;
            this.lstResults.ForeColor = ControlsBuilder.Foreground;
            this.lstResults.Font = new Font("Segoe UI", 9f);
            this.lstResults.BorderStyle = BorderStyle.FixedSingle;
            this.lstResults.Columns.Add("Application", 300);
            this.lstResults.Columns.Add("Category", 120);
            this.lstResults.Columns.Add("Kind", 110);
            this.lstResults.Columns.Add("Found in", 140);
            this.lstResults.DoubleClick += new EventHandler(this.lstResults_DoubleClick);

            this.lblProgress.Dock = DockStyle.Fill;
            this.lblProgress.ForeColor = ControlsBuilder.Muted;
            this.lblProgress.Font = new Font("Segoe UI", 9f);
            this.lblProgress.TextAlign = ContentAlignment.MiddleLeft;
            this.lblProgress.Text = "Choose which sources to scan, then press Scan.";

            this.cboPreset = ControlsBuilder.Dropdown(220);
            this.btnCreateGroup = ControlsBuilder.Button("Create group from selection", this.CreateGroup_Click, 180);
            this.btnCreateGroup.Enabled = false;
            this.btnCreatePreset = ControlsBuilder.Button("Create smart group", this.CreatePreset_Click, 140);
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
            rowOne.Controls.Add(ControlsBuilder.Body("Smart group rule:"));
            rowOne.Controls.Add(this.cboPreset);
            rowOne.Controls.Add(this.btnCreatePreset);

            var rowTwo = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty
            };
            rowTwo.Controls.Add(this.btnCreateGroup);
            rowTwo.Controls.Add(this.btnClose);

            this.actionBar.Controls.Add(rowOne);
            this.actionBar.Controls.Add(rowTwo);

            this.rootLayout.Dock = DockStyle.Fill;
            this.rootLayout.Padding = new Padding(12);
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.rootLayout.RowCount = 5;
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));

            this.rootLayout.Controls.Add(this.sourceBar, 0, 0);
            this.rootLayout.Controls.Add(this.filterBar, 0, 1);
            this.rootLayout.Controls.Add(this.lstResults, 0, 2);
            this.rootLayout.Controls.Add(this.lblProgress, 0, 3);
            this.rootLayout.Controls.Add(this.actionBar, 0, 4);

            ControlsBuilder.StyleForm(this, "Find applications", 860, 660);
            this.Controls.Add(this.rootLayout);
        }

        private static CheckBox SourceCheck(string text, bool value = false)
        {
            CheckBox box = ControlsBuilder.Check(text, value);
            box.Margin = new Padding(8, 6, 8, 6);
            return box;
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