namespace TaskbarGroups.App.Forms
{
    partial class frmMain
    {
        /// <summary>
        /// Required designer method.
        /// </summary>
        private void InitializeComponent()
        {
            this.Text = "Taskbar group";
            this.Name = "frmMain";
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(31, 31, 31);
            this.Size = new System.Drawing.Size(320, 120);
            this.Load += new System.EventHandler(this.frmMain_Load);
            this.Deactivate += new System.EventHandler(this.frmMain_Deactivate);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.frmMain_KeyDown);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.frmMain_KeyUp);
        }
    }
}