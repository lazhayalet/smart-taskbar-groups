using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TaskbarGroups.App.Services;
using TaskbarGroups.App.Views;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;

namespace TaskbarGroups.App.Forms
{
    /// <summary>
    /// The diagnostics page: what this machine looks like, what is wrong, and the
    /// actions that fix it.
    /// </summary>
    /// <remarks>
    /// Every check reports a status, a plain-language detail and, where one
    /// exists, the action that resolves it. The actions are real - they run
    /// through the same services the application uses - rather than advice to
    /// follow elsewhere.
    /// </remarks>
    public partial class frmDiagnostics : Form
    {
        private readonly ServiceContainer _services;
        private DiagnosticReport? _report;
        private CancellationTokenSource _work = new CancellationTokenSource();

        public frmDiagnostics(ServiceContainer services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            InitializeComponent();

            Refresh_Click(null, EventArgs.Empty);
        }

        private async void Refresh_Click(object? sender, EventArgs e)
        {
            SetBusy(true);
            btnRefresh.Enabled = false;
            lblStatus.Text = "Running checks...";

            try
            {
                _work.Cancel();
                _work.Dispose();
                _work = new CancellationTokenSource();

                _report = await _services.Diagnostics.BuildReportAsync(_work.Token);

                Render(_report);
                lblStatus.Text = "Checked " + _report.GeneratedAt.ToString("HH:mm:ss");
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Cancelled.";
            }
            catch (Exception ex)
            {
                _services.Logger.Log(SystemLogLevel.Error, "Diagnostics", "Report failed", ex);

                // A diagnostics page that throws is worse than useless, so the
                // failure is shown in place rather than raised.
                lstChecks.Items.Clear();
                lstChecks.Items.Add(new ListViewItem(new[] { "Failed", "-", "The checks could not be run: " + ex.Message }));
                lblStatus.Text = "Checks failed.";
            }
            finally
            {
                SetBusy(false);
                btnRefresh.Enabled = true;
            }
        }

        private void Render(DiagnosticReport report)
        {
            lstChecks.BeginUpdate();
            lstChecks.Items.Clear();

            foreach (HealthCheckResult check in report.Checks)
            {
                var item = new ListViewItem(new[] { check.Name, check.Status.ToString(), check.Detail });

                item.ForeColor = check.Status switch
                {
                    HealthStatus.Healthy => ControlsBuilder.Good,
                    HealthStatus.Warning => ControlsBuilder.Warning,
                    HealthStatus.Problem => ControlsBuilder.Danger,
                    _ => ControlsBuilder.Muted
                };

                if (!string.IsNullOrWhiteSpace(check.SuggestedAction))
                    item.SubItems[2].Text = check.Detail + "  ->  " + check.SuggestedAction;

                lstChecks.Items.Add(item);
            }

            lstChecks.EndUpdate();

            lstMonitors.BeginUpdate();
            lstMonitors.Items.Clear();

            foreach (MonitorInfo monitor in report.Monitors)
            {
                var item = new ListViewItem(new[]
                {
                    monitor.DeviceName,
                    monitor.Bounds.Width + "x" + monitor.Bounds.Height,
                    monitor.Bounds.X + "," + monitor.Bounds.Y,
                    Math.Round(monitor.ScaleFactor * 100) + "%",
                    monitor.TaskbarPosition.ToString(),
                    monitor.Primary ? "yes" : ""
                });

                if (monitor.Primary) item.Font = new Font(item.Font, FontStyle.Bold);
                lstMonitors.Items.Add(item);
            }

            lstMonitors.EndUpdate();

            lstShortcuts.BeginUpdate();
            lstShortcuts.Items.Clear();

            if (report.Shortcuts != null)
            {
                foreach (ShortcutHealthReport shortcut in report.Shortcuts.Items.Where(s => s.NeedsAttention))
                {
                    var item = new ListViewItem(new[]
                    {
                        shortcut.Name,
                        shortcut.Type.ToString(),
                        shortcut.Status.ToString(),
                        shortcut.Detail
                    });

                    item.ForeColor = shortcut.Status switch
                    {
                        ShortcutStatus.Missing => ControlsBuilder.Warning,
                        ShortcutStatus.InvalidShortcut => ControlsBuilder.Danger,
                        ShortcutStatus.PermissionDenied => ControlsBuilder.Danger,
                        _ => ControlsBuilder.Muted
                    };

                    lstShortcuts.Items.Add(item);
                }
            }

            lstShortcuts.EndUpdate();

            lblSummary.Text = Describe(report);
        }

        private static string Describe(DiagnosticReport report)
        {
            int problems = report.Checks.Count(c => c.Status == HealthStatus.Problem);
            int warnings = report.Checks.Count(c => c.Status == HealthStatus.Warning);
            int broken = report.Shortcuts?.Broken ?? 0;
            int missing = report.Shortcuts?.Missing ?? 0;

            var parts = new List<string> { report.ApplicationVersion, report.OsDescription };

            if (problems > 0) parts.Add(problems + " problem(s)");
            if (warnings > 0) parts.Add(warnings + " warning(s)");
            if (broken > 0) parts.Add(broken + " broken shortcut(s)");
            if (missing > 0) parts.Add(missing + " missing shortcut(s)");

            if (problems == 0 && warnings == 0 && broken == 0 && missing == 0) parts.Add("everything looks healthy");

            return string.Join("  |  ", parts);
        }

        private void SetBusy(bool busy)
        {
            UseWaitCursor = busy;
            tabControl.Enabled = !busy;
        }

        private void RepairDatabase_Click(object sender, EventArgs e)
        {
            try
            {
                if (!_services.Database.CheckIntegrity(out string before))
                {
                    MessageBox.Show(this,
                        "The database reports a problem:\n\n" + before +
                        "\n\nA backup is taken before anything is changed.",
                        "Database", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                // Repair is destructive, so it is backed up first - the same rule
                // the migration follows.
                string backup = _services.Backups.CreateBackup("before-repair");

                if (!_services.Database.Repair(out string error))
                {
                    MessageBox.Show(this, "The repair failed: " + error, "Database",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                MessageBox.Show(this,
                    "The database was checkpointed and vacuumed.\n\nA backup was written to:\n" + backup,
                    "Database", MessageBoxButtons.OK, MessageBoxIcon.Information);

                Refresh_Click(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Database", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RebuildIcons_Click(object sender, EventArgs e)
        {
            try
            {
                if (MessageBox.Show(this,
                        "This deletes every cached icon. They are extracted again on demand, which takes a moment the first time each group is opened.\n\nContinue?",
                        "Rebuild icon cache", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                _services.Icons.Clear();
                _services.Logger.Log(SystemLogLevel.Information, "Icons", "Icon cache rebuilt");

                MessageBox.Show(this, "The icon cache was cleared.", "Icons",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                Refresh_Click(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Icons", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void ValidateShortcuts_Click(object sender, EventArgs e)
        {
            btnValidate.Enabled = false;
            lblStatus.Text = "Validating shortcuts...";

            try
            {
                ShortcutValidationReport report = await _services.Diagnostics.ValidateShortcutsAsync(_work.Token);

                lstShortcuts.BeginUpdate();
                lstShortcuts.Items.Clear();

                foreach (ShortcutHealthReport shortcut in report.Items.Where(s => s.NeedsAttention))
                {
                    var item = new ListViewItem(new[]
                    {
                        shortcut.Name, shortcut.Type.ToString(), shortcut.Status.ToString(), shortcut.Detail
                    });

                    item.ForeColor = shortcut.Status == ShortcutStatus.Missing
                        ? ControlsBuilder.Warning
                        : ControlsBuilder.Danger;

                    lstShortcuts.Items.Add(item);
                }

                lstShortcuts.EndUpdate();

                lblStatus.Text = report.IsClean
                    ? "All " + report.Total + " shortcuts look healthy."
                    : report.Total + " checked: " + report.Missing + " missing, " + report.Broken + " broken, " +
                      report.Denied + " inaccessible.";
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Cancelled.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Validation failed: " + ex.Message;
            }
            finally
            {
                btnValidate.Enabled = true;
            }
        }

        private void Export_Click(object sender, EventArgs e)
        {
            try
            {
                if (_report == null)
                {
                    MessageBox.Show(this, "Run the checks first.", "Diagnostics",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string path = _services.Diagnostics.ExportReport(_report);

                if (string.IsNullOrEmpty(path))
                {
                    MessageBox.Show(this, "The report could not be written.", "Diagnostics",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                MessageBox.Show(this,
                    "The report was written to:\n" + path +
                    "\n\nA plain-text version is beside it, and the log files are in:\n" + _services.Paths.LogsDirectory,
                    "Diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenLogs_Click(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_services.Paths.LogsDirectory);

                Process.Start(new ProcessStartInfo
                {
                    FileName = _services.Paths.LogsDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "The logs folder could not be opened: " + ex.Message + "\n\nIt is at:\n" + _services.Paths.LogsDirectory,
                    "Logs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ResetPosition_Click(object sender, EventArgs e)
        {
            // A popup position is computed per monitor at open time and never
            // persisted, so there is nothing to reset. Saying so is better than
            // pretending to clear a setting that does not exist.
            MessageBox.Show(this,
                "Group popups are positioned from the monitor you clicked on every time they open, " +
                "so there is no stored position to reset.\n\nIf a popup has appeared in the wrong place, " +
                "check the monitor's scale and taskbar position are what you expect.",
                "Reset position", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Close_Click(object sender, EventArgs e)
        {
            _work.Cancel();
            Close();
        }

    }
}