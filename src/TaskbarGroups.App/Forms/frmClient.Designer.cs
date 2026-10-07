namespace TaskbarGroups.App.Forms
{
    partial class frmClient
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.TableLayoutPanel headerLayout;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblHeader;
        private System.Windows.Forms.Label lblVersion;
        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.ComboBox cboCategory;
        private System.Windows.Forms.Panel panelGroupsHost;
        private System.Windows.Forms.FlowLayoutPanel panelGroups;
        private System.Windows.Forms.Panel panelHelp;
        private System.Windows.Forms.Label lblHelpTitle;
        private System.Windows.Forms.FlowLayoutPanel panelActions;
        private System.Windows.Forms.Button cmdAddGroup;
        private System.Windows.Forms.Button cmdDiscovery;
        private System.Windows.Forms.Button cmdWorkspaces;
        private System.Windows.Forms.Button cmdBackup;
        private System.Windows.Forms.Button cmdSettings;
        private System.Windows.Forms.Button cmdDiagnostics;

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.rootLayout = new System.Windows.Forms.TableLayoutPanel();
            this.headerLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblHeader = new System.Windows.Forms.Label();
            this.lblVersion = new System.Windows.Forms.Label();
            this.txtSearch = new System.Windows.Forms.TextBox();
            this.cboCategory = new System.Windows.Forms.ComboBox();
            this.panelGroupsHost = new System.Windows.Forms.Panel();
            this.panelGroups = new System.Windows.Forms.FlowLayoutPanel();
            this.panelHelp = new System.Windows.Forms.Panel();
            this.lblHelpTitle = new System.Windows.Forms.Label();
            this.panelActions = new System.Windows.Forms.FlowLayoutPanel();
            this.cmdAddGroup = new System.Windows.Forms.Button();
            this.cmdDiscovery = new System.Windows.Forms.Button();
            this.cmdWorkspaces = new System.Windows.Forms.Button();
            this.cmdBackup = new System.Windows.Forms.Button();
            this.cmdSettings = new System.Windows.Forms.Button();
            this.cmdDiagnostics = new System.Windows.Forms.Button();

            this.SuspendLayout();

            //
            // lblTitle
            //
            this.lblTitle.AutoSize = true;
            this.lblTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 18F, System.Drawing.FontStyle.Bold);
            this.lblTitle.ForeColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.lblTitle.Text = "Taskbar Groups";
            this.lblTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            //
            // lblHeader
            //
            this.lblHeader.AutoSize = true;
            this.lblHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblHeader.ForeColor = System.Drawing.Color.FromArgb(170, 170, 170);
            this.lblHeader.Text = "";
            this.lblHeader.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            //
            // lblVersion
            //
            this.lblVersion.AutoSize = true;
            this.lblVersion.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblVersion.ForeColor = System.Drawing.Color.FromArgb(140, 140, 140);
            this.lblVersion.Text = "";
            this.lblVersion.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            //
            // txtSearch
            //
            this.txtSearch.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtSearch.PlaceholderText = "Search groups and shortcuts";
            this.txtSearch.Margin = new System.Windows.Forms.Padding(6, 8, 6, 8);
            this.txtSearch.TextChanged += new System.EventHandler(this.SearchBox_TextChanged);

            //
            // cboCategory
            //
            this.cboCategory.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cboCategory.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboCategory.Margin = new System.Windows.Forms.Padding(0, 8, 6, 8);
            this.cboCategory.SelectedIndexChanged += new System.EventHandler(this.CategoryFilter_SelectedIndexChanged);

            //
            // headerLayout
            //
            this.headerLayout.ColumnCount = 3;
            this.headerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 300F));
            this.headerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.headerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 240F));
            this.headerLayout.Controls.Add(this.lblTitle, 0, 0);
            this.headerLayout.Controls.Add(this.lblHeader, 1, 0);
            this.headerLayout.Controls.Add(this.lblVersion, 2, 0);
            this.headerLayout.Dock = System.Windows.Forms.DockStyle.Top;
            this.headerLayout.Padding = new System.Windows.Forms.Padding(16, 16, 16, 0);
            this.headerLayout.RowCount = 1;
            this.headerLayout.Size = new System.Drawing.Size(960, 56);
            this.headerLayout.TabIndex = 0;

            //
            // panelGroups
            //
            this.panelGroups.AutoScroll = true;
            this.panelGroups.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelGroups.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.panelGroups.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
            this.panelGroups.TabIndex = 1;
            this.panelGroups.WrapContents = false;

            //
            // panelGroupsHost
            //
            this.panelGroupsHost.Controls.Add(this.panelGroups);
            this.panelGroupsHost.Dock = System.Windows.Forms.DockStyle.Fill;

            //
            // lblHelpTitle
            //
            this.lblHelpTitle.AutoSize = true;
            this.lblHelpTitle.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblHelpTitle.ForeColor = System.Drawing.Color.FromArgb(150, 150, 150);
            this.lblHelpTitle.Padding = new System.Windows.Forms.Padding(0, 8, 0, 0);
            this.lblHelpTitle.Text = "Press \"Add Taskbar group\" to get started";
            this.lblHelpTitle.Visible = false;

            //
            // panelHelp
            //
            this.panelHelp.Controls.Add(this.lblHelpTitle);
            this.panelHelp.Dock = System.Windows.Forms.DockStyle.Bottom;

            ConfigureButton(this.cmdAddGroup, "Add taskbar group");
            ConfigureButton(this.cmdDiscovery, "Find applications");
            ConfigureButton(this.cmdWorkspaces, "Workspaces");
            ConfigureButton(this.cmdBackup, "Backup");
            ConfigureButton(this.cmdSettings, "Settings");
            ConfigureButton(this.cmdDiagnostics, "Diagnostics");

            this.cmdAddGroup.Click += (s, e) => this.AddGroup();
            this.cmdDiscovery.Click += (s, e) => this.ShowDiscovery();
            this.cmdWorkspaces.Click += new System.EventHandler(this.OpenWorkspacesItem_Click);
            this.cmdBackup.Click += (s, e) => this.ExportBackup();
            this.cmdSettings.Click += (s, e) => this.ShowSettingsPage();
            this.cmdDiagnostics.Click += (s, e) => this.ShowDiagnosticsPage();

            //
            // panelActions
            //
            this.panelActions.Controls.Add(this.cmdAddGroup);
            this.panelActions.Controls.Add(this.cmdDiscovery);
            this.panelActions.Controls.Add(this.cmdWorkspaces);
            this.panelActions.Controls.Add(this.cmdBackup);
            this.panelActions.Controls.Add(this.cmdSettings);
            this.panelActions.Controls.Add(this.cmdDiagnostics);
            this.panelActions.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelActions.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            this.panelActions.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
            this.panelActions.Size = new System.Drawing.Size(960, 56);

            //
            // rootLayout
            //
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.panelGroupsHost, 0, 1);
            this.rootLayout.Controls.Add(this.headerLayout, 0, 0);
            this.rootLayout.Controls.Add(this.panelActions, 0, 2);
            this.rootLayout.Controls.Add(this.panelHelp, 0, 3);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.RowCount = 4;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 56F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 56F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));

            //
            // frmClient
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(20, 20, 20);
            this.ClientSize = new System.Drawing.Size(960, 640);
            this.Controls.Add(this.rootLayout);
            this.ForeColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.MinimumSize = new System.Drawing.Size(640, 480);
            this.Name = "frmClient";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Taskbar Groups";
            this.Load += new System.EventHandler(this.frmClient_Load);
            this.rootLayout.ResumeLayout(false);
            this.headerLayout.ResumeLayout(false);
            this.headerLayout.PerformLayout();
            this.panelGroups.ResumeLayout(false);
            this.panelHelp.ResumeLayout(false);
            this.panelHelp.PerformLayout();
            this.panelActions.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        /// <summary>
        /// Shared button styling.
        /// </summary>
        /// <remarks>
        /// One helper rather than per-button property blocks: the dashboard is
        /// flat by design, and a repeated block per control would be six copies of
        /// the same numbers.
        /// </remarks>
        private static void ConfigureButton(System.Windows.Forms.Button button, string text)
        {
            button.AutoSize = true;
            button.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            button.ForeColor = System.Drawing.Color.FromArgb(240, 240, 240);
            button.BackColor = System.Drawing.Color.FromArgb(32, 32, 32);
            button.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(64, 64, 64);
            button.Margin = new System.Windows.Forms.Padding(4);
            button.Padding = new System.Windows.Forms.Padding(10, 4, 10, 4);
            button.Text = text;
            button.UseVisualStyleBackColor = false;
        }

        private void frmClient_Load(object sender, System.EventArgs e)
        {
            // The category filter is populated from the model's own list, so it
            // cannot drift from the enum.
            cboCategory.Items.Clear();
            cboCategory.Items.Add(new CategoryFilterItem(null, "All categories"));

            foreach (Core.Enums.AppCategory category in Discovery.ApplicationDiscovery.Categorizer.All)
                cboCategory.Items.Add(new CategoryFilterItem(category, Humanize(category.ToString())));

            cboCategory.SelectedIndex = 0;
        }

        /// <summary>Turns an enum name into something a person would read.</summary>
        private static string Humanize(string value)
        {
            var builder = new System.Text.StringBuilder(value.Length + 4);
            for (int index = 0; index < value.Length; index++)
            {
                char c = value[index];
                if (index > 0 && char.IsUpper(c)) builder.Append(' ');
                builder.Append(index == 0 ? c : char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }

        /// <summary>
        /// A category filter entry. Wrapping the enum keeps the combo's selected
        /// item typed, so the handler never has to re-parse a display string.
        /// </summary>
        private sealed class CategoryFilterItem
        {
            public CategoryFilterItem(Core.Enums.AppCategory? category, string label)
            {
                Category = category;
                Label = label;
            }

            public Core.Enums.AppCategory? Category { get; }

            public string Label { get; }

            public override string ToString() => Label;
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