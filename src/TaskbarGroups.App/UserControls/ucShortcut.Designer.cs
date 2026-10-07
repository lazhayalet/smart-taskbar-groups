namespace TaskbarGroups.App.UserControls
{
    partial class ucShortcut
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.PictureBox picIcon;
        private System.Windows.Forms.ToolTip toolTip;

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.picIcon = new System.Windows.Forms.PictureBox();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.picIcon)).BeginInit();
            this.SuspendLayout();

            //
            // picIcon
            //
            this.picIcon.BackColor = System.Drawing.Color.Transparent;
            this.picIcon.Location = new System.Drawing.Point(4, 4);
            this.picIcon.Name = "picIcon";
            this.picIcon.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picIcon.Size = new System.Drawing.Size(48, 48);
            this.picIcon.TabStop = false;

            //
            // toolTip
            //
            this.toolTip.AutoPopDelay = 10000;
            this.toolTip.InitialDelay = 250;
            this.toolTip.ReshowDelay = 100;
            this.toolTip.ShowAlways = true;

            //
            // ucShortcut
            //
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.Transparent;
            this.Controls.Add(this.picIcon);
            this.Name = "ucShortcut";
            this.Size = new System.Drawing.Size(56, 56);
            this.TabStop = true;
            ((System.ComponentModel.ISupportInitialize)(this.picIcon)).EndInit();
            this.ResumeLayout(false);
        }

    }
}