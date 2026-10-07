using System;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGroups.Core.Enums;

namespace TaskbarGroups.App.Forms
{
    partial class frmGroup
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.TableLayoutPanel headerLayout;
        private System.Windows.Forms.Panel panelGroupIcon;
        private System.Windows.Forms.Button cmdAddGroupIcon;
        private System.Windows.Forms.Label lblAddGroupIcon;
        private System.Windows.Forms.Label lblErrorIcon;
        private System.Windows.Forms.TextBox txtGroupName;
        private System.Windows.Forms.Label lblErrorName;
        private System.Windows.Forms.Panel panelShortcuts;
        private System.Windows.Forms.Panel panelAddShortcut;
        private System.Windows.Forms.Label lblAddShortcutHint;
        private System.Windows.Forms.Label lblErrorShortcuts;

        private System.Windows.Forms.Panel panelArguments;
        private System.Windows.Forms.Label lblArguments;
        private System.Windows.Forms.TextBox txtArguments;
        private System.Windows.Forms.Label lblWorkingDirectory;
        private System.Windows.Forms.TextBox txtWorkingDirectory;
        private System.Windows.Forms.Button cmdSelectDirectory;
        private System.Windows.Forms.CheckBox chkRunAsAdministrator;

        private System.Windows.Forms.Panel panelColor;
        private System.Windows.Forms.TableLayoutPanel settingsLayout;
        private System.Windows.Forms.Label lblWidth;
        private System.Windows.Forms.Button cmdWidthDown;
        private System.Windows.Forms.Label lblWidthValue;
        private System.Windows.Forms.Button cmdWidthUp;
        private System.Windows.Forms.Label lblErrorWidth;
        private System.Windows.Forms.Label lblOpacity;
        private System.Windows.Forms.Button cmdOpacityDown;
        private System.Windows.Forms.Label lblOpacityValue;
        private System.Windows.Forms.Button cmdOpacityUp;
        private System.Windows.Forms.Label lblErrorOpacity;
        private System.Windows.Forms.Label lblTheme;
        private System.Windows.Forms.ComboBox cboTheme;
        private System.Windows.Forms.CheckBox pnlAllowOpenAll;
        private System.Windows.Forms.Label lblLaunchAllHint;

        private System.Windows.Forms.FlowLayoutPanel panelButtons;
        private System.Windows.Forms.Button cmdSave;
        private System.Windows.Forms.Button cmdDelete;
        private System.Windows.Forms.Button cmdExit;

        private static readonly System.Drawing.Color Surface = System.Drawing.Color.FromArgb(24, 24, 24);
        private static readonly System.Drawing.Color SurfaceRaised = System.Drawing.Color.FromArgb(32, 32, 32);
        private static readonly System.Drawing.Color Foreground = System.Drawing.Color.FromArgb(240, 240, 240);
        private static readonly System.Drawing.Color Muted = System.Drawing.Color.FromArgb(160, 160, 160);
        private static readonly System.Drawing.Color Danger = System.Drawing.Color.FromArgb(255, 120, 120);

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.rootLayout = new System.Windows.Forms.TableLayoutPanel();
            this.headerLayout = new System.Windows.Forms.TableLayoutPanel();
            this.panelGroupIcon = new System.Windows.Forms.Panel();
            this.cmdAddGroupIcon = new System.Windows.Forms.Button();
            this.lblAddGroupIcon = new System.Windows.Forms.Label();
            this.lblErrorIcon = new System.Windows.Forms.Label();
            this.txtGroupName = new System.Windows.Forms.TextBox();
            this.lblErrorName = new System.Windows.Forms.Label();
            this.panelShortcuts = new System.Windows.Forms.Panel();
            this.panelAddShortcut = new System.Windows.Forms.Panel();
                        this.lblAddShortcutHint = new System.Windows.Forms.Label();
            this.lblErrorShortcuts = new System.Windows.Forms.Label();
            this.panelArguments = new System.Windows.Forms.Panel();
            this.lblArguments = new System.Windows.Forms.Label();
            this.txtArguments = new System.Windows.Forms.TextBox();
            this.lblWorkingDirectory = new System.Windows.Forms.Label();
            this.txtWorkingDirectory = new System.Windows.Forms.TextBox();
            this.cmdSelectDirectory = new System.Windows.Forms.Button();
            this.chkRunAsAdministrator = new System.Windows.Forms.CheckBox();
            this.panelColor = new System.Windows.Forms.Panel();
            this.settingsLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblWidth = new System.Windows.Forms.Label();
            this.cmdWidthDown = new System.Windows.Forms.Button();
            this.lblWidthValue = new System.Windows.Forms.Label();
            this.cmdWidthUp = new System.Windows.Forms.Button();
            this.lblErrorWidth = new System.Windows.Forms.Label();
            this.lblOpacity = new System.Windows.Forms.Label();
            this.cmdOpacityDown = new System.Windows.Forms.Button();
            this.lblOpacityValue = new System.Windows.Forms.Label();
            this.cmdOpacityUp = new System.Windows.Forms.Button();
            this.lblErrorOpacity = new System.Windows.Forms.Label();
            this.lblTheme = new System.Windows.Forms.Label();
            this.cboTheme = new System.Windows.Forms.ComboBox();
            this.pnlAllowOpenAll = new System.Windows.Forms.CheckBox();
            this.lblLaunchAllHint = new System.Windows.Forms.Label();
            this.panelButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.cmdSave = new System.Windows.Forms.Button();
            this.cmdDelete = new System.Windows.Forms.Button();
            this.cmdExit = new System.Windows.Forms.Button();

            this.SuspendLayout();

            ConfigureButton(this.cmdAddGroupIcon, size: new System.Drawing.Size(64, 64), icon: true);
            this.cmdAddGroupIcon.FlatAppearance.BorderSize = 1;
            this.cmdAddGroupIcon.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(64, 64, 64);
            this.cmdAddGroupIcon.DragEnter += new System.Windows.Forms.DragEventHandler(this.PanelGroupIcon_DragEnter);
            this.cmdAddGroupIcon.DragDrop += new System.Windows.Forms.DragEventHandler(this.PanelGroupIcon_DragDrop);
            this.cmdAddGroupIcon.Click += new System.EventHandler(this.AddGroupIcon_Click);

            this.lblAddGroupIcon.AutoSize = true;
            this.lblAddGroupIcon.ForeColor = Muted;
            this.lblAddGroupIcon.Location = new System.Drawing.Point(0, 68);
            this.lblAddGroupIcon.Text = "Add group icon";
            this.lblAddGroupIcon.Width = 64;
            this.lblAddGroupIcon.TextAlign = System.Drawing.ContentAlignment.TopCenter;

            this.lblErrorIcon.AutoSize = true;
            this.lblErrorIcon.ForeColor = Danger;
            this.lblErrorIcon.Location = new System.Drawing.Point(72, 72);
            this.lblErrorIcon.Size = new System.Drawing.Size(200, 16);
            this.lblErrorIcon.Text = "";
            this.lblErrorIcon.Visible = false;

            this.panelGroupIcon.Controls.Add(this.cmdAddGroupIcon);
            this.panelGroupIcon.Controls.Add(this.lblAddGroupIcon);
            this.panelGroupIcon.Controls.Add(this.lblErrorIcon);
            this.panelGroupIcon.Size = new System.Drawing.Size(300, 92);

            this.txtGroupName.Dock = System.Windows.Forms.DockStyle.Top;
            this.txtGroupName.Font = new System.Drawing.Font("Segoe UI", 12F);
            this.txtGroupName.ForeColor = Foreground;
            this.txtGroupName.BackColor = SurfaceRaised;
            this.txtGroupName.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtGroupName.Text = PlaceholderText;
            this.txtGroupName.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this.txtGroupName.TextChanged += new System.EventHandler(this.GroupName_TextChanged);
            this.txtGroupName.Click += new System.EventHandler(this.GroupName_Click);
            this.txtGroupName.Leave += new System.EventHandler(this.GroupName_Leave);

            this.lblErrorName.AutoSize = true;
            this.lblErrorName.ForeColor = Danger;
            this.lblErrorName.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblErrorName.Height = 18;
            this.lblErrorName.Text = "";
            this.lblErrorName.Visible = false;

            this.headerLayout.ColumnCount = 2;
            this.headerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 300F));
            this.headerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.headerLayout.Controls.Add(this.panelGroupIcon, 0, 0);
            this.headerLayout.Controls.Add(this.txtGroupName, 1, 0);
            this.headerLayout.Controls.Add(this.lblErrorName, 1, 1);
            this.headerLayout.Dock = System.Windows.Forms.DockStyle.Top;
            this.headerLayout.Padding = new System.Windows.Forms.Padding(16, 12, 16, 4);
            this.headerLayout.RowCount = 2;


            this.panelAddShortcut.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelAddShortcut.Height = 50;
            this.panelAddShortcut.BackColor = SurfaceRaised;
            this.panelAddShortcut.Cursor = System.Windows.Forms.Cursors.Hand;
            this.panelAddShortcut.Padding = new System.Windows.Forms.Padding(8);
            this.panelAddShortcut.AllowDrop = true;
            this.panelAddShortcut.DragEnter += new System.Windows.Forms.DragEventHandler(this.PanelAddShortcut_DragEnter);
            this.panelAddShortcut.DragOver += new System.Windows.Forms.DragEventHandler(this.PanelAddShortcut_DragOver);
            this.panelAddShortcut.DragDrop += new System.Windows.Forms.DragEventHandler(this.PanelAddShortcut_DragDrop);
            this.panelAddShortcut.Click += new System.EventHandler(this.AddShortcut_Click);
            this.panelAddShortcut.Paint += PaintAddShortcut;

            this.lblAddShortcutHint.AutoSize = true;
            this.lblAddShortcutHint.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblAddShortcutHint.ForeColor = Muted;
            this.lblAddShortcutHint.Location = new System.Drawing.Point(12, 16);
            this.lblAddShortcutHint.Text = "Add shortcuts...   (click, or drop .exe / .lnk / .url / scripts / folders / Store apps)";

            this.panelShortcuts.AllowDrop = true;
            this.panelShortcuts.AutoScroll = true;
            this.panelShortcuts.BackColor = System.Drawing.Color.FromArgb(18, 18, 18);
            this.panelShortcuts.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelShortcuts.Padding = new System.Windows.Forms.Padding(8);

            this.lblErrorShortcuts.AutoSize = true;
            this.lblErrorShortcuts.ForeColor = Danger;
            this.lblErrorShortcuts.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.lblErrorShortcuts.Height = 18;
            this.lblErrorShortcuts.Text = "";
            this.lblErrorShortcuts.Visible = false;

            this.lblArguments.AutoSize = true;
            this.lblArguments.ForeColor = Muted;
            this.lblArguments.Text = "Arguments";
            this.txtArguments.Dock = System.Windows.Forms.DockStyle.Top;
            this.txtArguments.ForeColor = Foreground;
            this.txtArguments.BackColor = SurfaceRaised;
            this.txtArguments.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtArguments.Enabled = false;
            this.txtArguments.TextChanged += new System.EventHandler(this.Arguments_Changed);

            this.lblWorkingDirectory.AutoSize = true;
            this.lblWorkingDirectory.ForeColor = Muted;
            this.lblWorkingDirectory.Text = "Working directory";
            this.txtWorkingDirectory.Dock = System.Windows.Forms.DockStyle.Top;
            this.txtWorkingDirectory.ForeColor = Foreground;
            this.txtWorkingDirectory.BackColor = SurfaceRaised;
            this.txtWorkingDirectory.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtWorkingDirectory.Enabled = false;
            this.txtWorkingDirectory.TextChanged += new System.EventHandler(this.WorkingDirectory_Changed);

            ConfigureButton(this.cmdSelectDirectory, "Choose...", new System.Drawing.Size(90, 26));
            this.cmdSelectDirectory.Dock = System.Windows.Forms.DockStyle.Left;
            this.cmdSelectDirectory.Enabled = false;
            this.cmdSelectDirectory.Click += new System.EventHandler(this.SelectDirectory_Click);

            this.chkRunAsAdministrator.AutoSize = true;
            this.chkRunAsAdministrator.ForeColor = Foreground;
            this.chkRunAsAdministrator.Text = "Run as administrator (asks for permission each time)";
            this.chkRunAsAdministrator.Dock = System.Windows.Forms.DockStyle.Top;
            this.chkRunAsAdministrator.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);
            this.chkRunAsAdministrator.CheckedChanged += new System.EventHandler(this.EnableAdministrator);

            this.panelArguments.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelArguments.BackColor = System.Drawing.Color.FromArgb(18, 18, 18);
            this.panelArguments.Visible = false;
            this.panelArguments.Controls.Add(this.chkRunAsAdministrator);
            this.panelArguments.Controls.Add(this.txtWorkingDirectory);
            this.panelArguments.Controls.Add(this.lblWorkingDirectory);
            this.panelArguments.Controls.Add(this.cmdSelectDirectory);
            this.panelArguments.Controls.Add(this.txtArguments);
            this.panelArguments.Controls.Add(this.lblArguments);

            ConfigureLabel(this.lblWidth, "Shortcuts per row");
            ConfigureStepper(this.cmdWidthDown, "-");
            ConfigureStepper(this.cmdWidthUp, "+");
            ConfigureValue(this.lblWidthValue, "5");
            ConfigureLabel(this.lblErrorWidth, string.Empty, Danger, 160);

            ConfigureLabel(this.lblOpacity, "Transparency");
            ConfigureStepper(this.cmdOpacityDown, "-");
            ConfigureStepper(this.cmdOpacityUp, "+");
            ConfigureValue(this.lblOpacityValue, "10");
            ConfigureLabel(this.lblErrorOpacity, string.Empty, Danger, 160);

            this.cmdWidthUp.Click += new System.EventHandler(this.WidthUp_Click);
            this.cmdWidthDown.Click += new System.EventHandler(this.WidthDown_Click);
            this.cmdOpacityUp.Click += new System.EventHandler(this.OpacityUp_Click);
            this.cmdOpacityDown.Click += new System.EventHandler(this.OpacityDown_Click);

            ConfigureLabel(this.lblTheme, "Theme");
            this.cboTheme.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cboTheme.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboTheme.ForeColor = Foreground;
            this.cboTheme.BackColor = SurfaceRaised;
            this.cboTheme.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cboTheme.SelectedIndexChanged += new System.EventHandler(this.Theme_SelectedIndexChanged);
            PopulateThemes();

            this.pnlAllowOpenAll.AutoSize = true;
            this.pnlAllowOpenAll.ForeColor = Foreground;
            this.pnlAllowOpenAll.Text = "Allow Ctrl+Enter to open everything in this group";
            this.pnlAllowOpenAll.CheckedChanged += new System.EventHandler(this.AllowOpenAll_Changed);

            ConfigureLabel(this.lblLaunchAllHint,
                "Windows 11 does not allow an application to pin a shortcut to the taskbar for you. " +
                "The shortcut is created for you; right-click it on the taskbar and choose \"Pin to taskbar\".",
                Muted, 480);

            this.settingsLayout.ColumnCount = 4;
            this.settingsLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 120F));
            this.settingsLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.settingsLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            this.settingsLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.settingsLayout.Controls.Add(this.lblWidth, 0, 0);
            this.settingsLayout.Controls.Add(this.cmdWidthDown, 1, 0);
            this.settingsLayout.Controls.Add(this.lblWidthValue, 2, 0);
            this.settingsLayout.Controls.Add(this.cmdWidthUp, 1, 1);
            this.settingsLayout.Controls.Add(this.lblErrorWidth, 3, 0);
            this.settingsLayout.Controls.Add(this.lblOpacity, 0, 2);
            this.settingsLayout.Controls.Add(this.cmdOpacityDown, 1, 2);
            this.settingsLayout.Controls.Add(this.lblOpacityValue, 2, 2);
            this.settingsLayout.Controls.Add(this.cmdOpacityUp, 1, 3);
            this.settingsLayout.Controls.Add(this.lblErrorOpacity, 3, 2);
            this.settingsLayout.Controls.Add(this.lblTheme, 0, 4);
            this.settingsLayout.Controls.Add(this.cboTheme, 1, 4);
            this.settingsLayout.SetColumnSpan(this.cboTheme, 3);
            this.settingsLayout.Controls.Add(this.pnlAllowOpenAll, 0, 5);
            this.settingsLayout.SetColumnSpan(this.pnlAllowOpenAll, 4);
            this.settingsLayout.Controls.Add(this.lblLaunchAllHint, 0, 6);
            this.settingsLayout.SetColumnSpan(this.lblLaunchAllHint, 4);
            this.settingsLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.settingsLayout.Padding = new System.Windows.Forms.Padding(16, 8, 16, 8);

            this.panelColor.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelColor.BackColor = System.Drawing.Color.FromArgb(18, 18, 18);
            this.panelColor.Controls.Add(this.settingsLayout);

            ConfigureButton(this.cmdSave, "Save", new System.Drawing.Size(96, 32));
            ConfigureButton(this.cmdDelete, "Delete", new System.Drawing.Size(96, 32));
            ConfigureButton(this.cmdExit, "Cancel", new System.Drawing.Size(96, 32));
            this.cmdDelete.ForeColor = Danger;

            this.cmdSave.Click += new System.EventHandler(this.Save_Click);
            this.cmdDelete.Click += new System.EventHandler(this.Delete_Click);
            this.cmdExit.Click += new System.EventHandler(this.Exit_Click);

            this.panelButtons.Controls.Add(this.cmdSave);
            this.panelButtons.Controls.Add(this.cmdDelete);
            this.panelButtons.Controls.Add(this.cmdExit);
            this.panelButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelButtons.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.panelButtons.Height = 52;
            this.panelButtons.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
            this.panelButtons.WrapContents = false;

            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.panelShortcuts, 0, 0);
            this.rootLayout.Controls.Add(this.panelAddShortcut, 0, 1);
            this.rootLayout.Controls.Add(this.lblErrorShortcuts, 0, 2);
            this.rootLayout.Controls.Add(this.panelArguments, 0, 3);
            this.rootLayout.Controls.Add(this.panelColor, 0, 3);
            this.rootLayout.Controls.Add(this.headerLayout, 0, 4);
            this.rootLayout.Controls.Add(this.panelButtons, 0, 5);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.RowCount = 6;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 50F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 140F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 104F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 52F));

            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(20, 20, 20);
            this.ClientSize = new System.Drawing.Size(760, 700);
            this.Controls.Add(this.rootLayout);
            this.ForeColor = Foreground;
            this.MinimumSize = new System.Drawing.Size(560, 560);
            this.Name = "frmGroup";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Load += new System.EventHandler(this.frmGroup_Load);
            this.Click += new System.EventHandler(this.Form_Click);

            this.rootLayout.ResumeLayout(false);
            this.headerLayout.ResumeLayout(false);
            this.panelGroupIcon.ResumeLayout(false);
            this.panelGroupIcon.PerformLayout();
            this.panelShortcuts.ResumeLayout(false);
            this.panelArguments.ResumeLayout(false);
            this.panelArguments.PerformLayout();
            this.settingsLayout.ResumeLayout(false);
            this.settingsLayout.PerformLayout();
            this.panelColor.ResumeLayout(false);
            this.panelColor.PerformLayout();
            this.panelButtons.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        /// <summary>
        /// Shared control styling.
        /// </summary>
        /// <remarks>
        /// The 1.x designer expressed the flat dark look as a repeated property
        /// block on every control. The result was the same numbers copied a dozen
        /// times, and changing the palette meant changing all of them. These
        /// helpers keep the palette in one place.
        /// </remarks>
        private static void ConfigureButton(System.Windows.Forms.Button button, string text = "", System.Drawing.Size? size = null, bool icon = false)
        {
            button.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            button.ForeColor = Foreground;
            button.BackColor = SurfaceRaised;
            button.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(64, 64, 64);
            button.UseVisualStyleBackColor = false;
            button.Text = text;

            if (size.HasValue)
            {
                button.Size = size.Value;
                button.AutoSize = false;
            }
            else
            {
                button.AutoSize = true;
                button.Padding = new System.Windows.Forms.Padding(10, 4, 10, 4);
            }
        }

        private static void ConfigureLabel(System.Windows.Forms.Label label, string text,
            System.Drawing.Color? color = null, int width = 0)
        {
            label.AutoSize = width == 0;
            label.Text = text;
            label.ForeColor = color ?? Foreground;

            if (width > 0)
            {
                label.AutoSize = false;
                label.Size = new System.Drawing.Size(width, 18);
            }

            label.Margin = new System.Windows.Forms.Padding(3, 6, 3, 6);
        }

        private static void ConfigureStepper(System.Windows.Forms.Button button, string text)
        {
            button.Text = text;
            button.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            button.ForeColor = Foreground;
            button.BackColor = SurfaceRaised;
            button.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(64, 64, 64);
            button.UseVisualStyleBackColor = false;
            button.Size = new System.Drawing.Size(28, 26);
            button.Margin = new System.Windows.Forms.Padding(3, 3, 3, 3);
        }

        private static void ConfigureValue(System.Windows.Forms.Label label, string text)
        {
            label.Text = text;
            label.AutoSize = false;
            label.Size = new System.Drawing.Size(40, 26);
            label.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            label.ForeColor = Foreground;
            label.BackColor = SurfaceRaised;
            label.Margin = new System.Windows.Forms.Padding(3, 3, 3, 3);
        }

        private void PopulateThemes()
        {
            cboTheme.Items.Add(new ThemeOption(GroupTheme.System, "Follow Windows"));
            cboTheme.Items.Add(new ThemeOption(GroupTheme.Dark, "Dark"));
            cboTheme.Items.Add(new ThemeOption(GroupTheme.Light, "Light"));
            cboTheme.Items.Add(new ThemeOption(GroupTheme.Mica, "Mica (Windows 11)"));
            cboTheme.Items.Add(new ThemeOption(GroupTheme.Acrylic, "Acrylic (Windows 11)"));
            cboTheme.SelectedIndex = 0;
        }

        /// <summary>
        /// Draws the add-shortcuts strip.
        /// </summary>
        /// <remarks>
        /// Drawn rather than composed from a picture box, so the hover state is a
        /// colour change with no image swap and no GDI churn.
        /// </remarks>
        private void PaintAddShortcut(object sender, System.Windows.Forms.PaintEventArgs e)
        {
            System.Drawing.Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            bool hovering = panelAddShortcut.ClientRectangle.Contains(panelAddShortcut.PointToClient(System.Windows.Forms.Cursor.Position));
            g.FillRectangle(new System.Drawing.SolidBrush(hovering ? System.Drawing.Color.FromArgb(45, 45, 45) : SurfaceRaised),
                panelAddShortcut.ClientRectangle);

            using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(90, 90, 90)))
            using (var font = new System.Drawing.Font("Segoe UI", 9.5f))
            using (var brush = new System.Drawing.SolidBrush(Foreground))
            {
                g.DrawRectangle(pen, 1, 1, panelAddShortcut.Width - 3, panelAddShortcut.Height - 3);
                g.DrawString("+  Add shortcuts", font, brush, 14, panelAddShortcut.Height / 2 - 10);
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
