using System;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGroups.App.Views;

namespace TaskbarGroups.App.Forms
{
    partial class frmWorkspaces
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel rootLayout;
        private SplitContainer split;

        private ListView lstWorkspaces;
        private FlowLayoutPanel workspaceBar;

        private Button btnNew;
        private Button btnLaunch;
        private Button btnDuplicate;
        private Button btnDelete;

        private ListView lstItems;
        private FlowLayoutPanel itemBar;

        private Button btnAddItem;
        private Button btnEditItem;
        private Button btnRemoveItem;
        private Button btnCapture;
        private Button btnClose;

        private Label lblStatus;

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.rootLayout = new TableLayoutPanel();
            this.split = new SplitContainer();
            this.lstWorkspaces = new ListView();
            this.lstItems = new ListView();
            this.workspaceBar = new FlowLayoutPanel();
            this.itemBar = new FlowLayoutPanel();
            this.lblStatus = new Label();

            this.btnNew = ControlsBuilder.Button("New", this.New_Click, 88, primary: true);
            this.btnLaunch = ControlsBuilder.Button("Launch", this.Launch_Click, 88);
            this.btnDuplicate = ControlsBuilder.Button("Duplicate", this.Duplicate_Click, 88);
            this.btnDelete = ControlsBuilder.Button("Delete", this.Delete_Click, 88);

            this.btnAddItem = ControlsBuilder.Button("Add application...", this.AddItem_Click, 130);
            this.btnEditItem = ControlsBuilder.Button("Placement...", this.EditItem_Click, 110);
            this.btnRemoveItem = ControlsBuilder.Button("Remove", this.RemoveItem_Click, 88);
            this.btnCapture = ControlsBuilder.Button("Save current layout", this.Capture_Click, 150);
            this.btnClose = ControlsBuilder.Button("Close", this.Close_Click, 88);

            ConfigureList(this.lstWorkspaces, new[] { "Workspace", "Items", "Delay", "Description" }, 180, 60, 80, 140);
            this.lstWorkspaces.SelectedIndexChanged += new EventHandler(this.lstWorkspaces_SelectedIndexChanged);

            ConfigureList(this.lstItems, new[] { "Window", "Application", "Monitor", "Position", "Size" }, 160, 130, 150, 110, 110);
            this.lstItems.DoubleClick += new EventHandler(this.EditItem_Click);

            this.workspaceBar.Dock = DockStyle.Top;
            this.workspaceBar.FlowDirection = FlowDirection.LeftToRight;
            this.workspaceBar.WrapContents = false;
            this.workspaceBar.Height = 44;
            this.workspaceBar.Padding = new Padding(2, 4, 2, 4);
            this.workspaceBar.Controls.Add(this.btnNew);
            this.workspaceBar.Controls.Add(this.btnLaunch);
            this.workspaceBar.Controls.Add(this.btnDuplicate);
            this.workspaceBar.Controls.Add(this.btnDelete);

            this.itemBar.Dock = DockStyle.Bottom;
            this.itemBar.FlowDirection = FlowDirection.LeftToRight;
            this.itemBar.WrapContents = true;
            this.itemBar.Height = 76;
            this.itemBar.Padding = new Padding(2, 4, 2, 4);
            this.itemBar.Controls.Add(this.btnAddItem);
            this.itemBar.Controls.Add(this.btnEditItem);
            this.itemBar.Controls.Add(this.btnRemoveItem);
            this.itemBar.Controls.Add(this.btnCapture);
            this.itemBar.Controls.Add(this.btnClose);

            var leftPanel = new Panel { Dock = DockStyle.Fill };
            leftPanel.Controls.Add(this.lstWorkspaces);
            leftPanel.Controls.Add(this.workspaceBar);

            var rightPanel = new Panel { Dock = DockStyle.Fill };
            rightPanel.Controls.Add(this.lstItems);
            rightPanel.Controls.Add(this.itemBar);

            this.split.Dock = DockStyle.Fill;
            this.split.Orientation = Orientation.Vertical;
            this.split.Size = new Size(1000, 600);
            this.split.Panel1MinSize = 260;
            this.split.Panel2MinSize = 320;
            this.split.SplitterDistance = 380;
            this.split.Panel1.Controls.Add(leftPanel);
            this.split.Panel2.Controls.Add(rightPanel);

            this.lblStatus.Dock = DockStyle.Fill;
            this.lblStatus.ForeColor = ControlsBuilder.Muted;
            this.lblStatus.Font = new Font("Segoe UI", 9f);
            this.lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            this.lblStatus.Text =
                "A workspace opens a set of applications and arranges their windows on chosen monitors.";

            this.rootLayout.Dock = DockStyle.Fill;
            this.rootLayout.Padding = new Padding(12);
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.rootLayout.RowCount = 2;
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            this.rootLayout.Controls.Add(this.split, 0, 0);
            this.rootLayout.Controls.Add(this.lblStatus, 0, 1);

            ControlsBuilder.StyleForm(this, "Workspaces", 900, 620);
            this.Controls.Add(this.rootLayout);
        }

        private static void ConfigureList(ListView list, string[] columns, params int[] widths)
        {
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = true;
            list.GridLines = true;
            list.BackColor = ControlsBuilder.Panel;
            list.ForeColor = ControlsBuilder.Foreground;
            list.Font = new Font("Segoe UI", 9f);
            list.BorderStyle = BorderStyle.FixedSingle;

            for (int index = 0; index < columns.Length; index++)
                list.Columns.Add(columns[index], widths[index]);
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