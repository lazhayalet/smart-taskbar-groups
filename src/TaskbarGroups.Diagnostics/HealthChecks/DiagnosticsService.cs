using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Data.Storage;
using TaskbarGroups.Windows.Dpi;
using TaskbarGroups.Windows.Monitor;

namespace TaskbarGroups.Diagnostics.HealthChecks
{
    /// <summary>
    /// Self-checks and report generation.
    /// </summary>
    /// <remarks>
    /// The point of this class is that a user can answer "why is it doing that?"
    /// without reading a log file. Each check returns a status, a plain-language
    /// detail and, where one exists, the action that would fix it. Checks never
    /// throw: a diagnostic page that crashes is worse than one that reports a
    /// failed check.
    /// </remarks>
    public sealed class DiagnosticsService : IDiagnosticsService
    {
        private readonly Database _database;
        private readonly DataPaths _paths;
        private readonly IGroupRepository _groups;
        private readonly IIconCache _icons;
        private readonly MonitorService _monitors;
        private readonly AppSettings _settings;
        private readonly IAppLogger? _logger;
        private readonly bool _isPortable;

        public DiagnosticsService(
            Database database,
            DataPaths paths,
            IGroupRepository groups,
            IIconCache icons,
            MonitorService monitors,
            AppSettings settings,
            bool isPortable,
            IAppLogger? logger = null)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _groups = groups ?? throw new ArgumentNullException(nameof(groups));
            _icons = icons ?? throw new ArgumentNullException(nameof(icons));
            _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _isPortable = isPortable;
            _logger = logger;
        }

        public async Task<DiagnosticReport> BuildReportAsync(CancellationToken cancellationToken = default)
        {
            var report = new DiagnosticReport
            {
                ApplicationVersion = ApplicationVersion(),
                OsDescription = RuntimeInformation.OSDescription,
                OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                IsPortable = _isPortable,
                DataDirectory = _paths.BaseDirectory,
                DpiAwarenessContext = DpiService.DescribeAwareness(),
                DpiAware = IsPerMonitorAware()
            };

            MonitorQueryResult monitors = await Task.Run(() => _monitors.GetMonitors(), cancellationToken).ConfigureAwait(false);
            report.Monitors.AddRange(monitors.Monitors);

            if (_logger is FileLogger fileLogger)
                report.LogPaths.AddRange(fileLogger.DescribeFiles());

            await Task.Run(() => report.Shortcuts = ValidateShortcutsNow(cancellationToken), cancellationToken).ConfigureAwait(false);

            await Task.Run(() =>
            {
                report.Checks.Add(CheckDatabase());
                report.Checks.Add(CheckDataDirectory());
                report.Checks.Add(CheckIconCache());
                report.Checks.Add(CheckMonitors(monitors));
                report.Checks.Add(CheckDpi());
                report.Checks.Add(CheckShortcutHealth(report.Shortcuts));
                report.Checks.Add(CheckShortcutsFolder());
                report.Checks.Add(CheckLegacyMigration());
            }, cancellationToken).ConfigureAwait(false);

            return report;
        }

        public async Task<ShortcutValidationReport> ValidateShortcutsAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => ValidateShortcutsNow(cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Classifies every stored shortcut without launching anything.
        /// </summary>
        private ShortcutValidationReport ValidateShortcutsNow(CancellationToken cancellationToken)
        {
            var report = new ShortcutValidationReport();

            foreach (Group group in _groups.GetAll())
            {
                foreach (ShortcutItem item in _groups.GetShortcuts(group.Id))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        ShortcutStatus status = ShortcutValidator.Classify(item);

                        report.Items.Add(new ShortcutHealthReport
                        {
                            ShortcutId = item.Id,
                            Name = string.IsNullOrWhiteSpace(item.Name) ? item.Target : item.Name,
                            Target = item.Target,
                            Type = item.Type,
                            Status = status,
                            Detail = DescribeStatus(status, item)
                        });
                    }
                    catch (Exception ex)
                    {
                        // Classification must never take the page down.
                        report.Items.Add(new ShortcutHealthReport
                        {
                            ShortcutId = item.Id,
                            Name = item.Name,
                            Target = item.Target,
                            Type = item.Type,
                            Status = ShortcutStatus.Unknown,
                            Detail = ex.Message
                        });
                    }
                }
            }

            return report;
        }

        private static string DescribeStatus(ShortcutStatus status, ShortcutItem item)
        {
            switch (status)
            {
                case ShortcutStatus.Valid:
                    return "Ready";

                case ShortcutStatus.Missing:
                    return "The target no longer exists: " + item.Target;

                case ShortcutStatus.PermissionDenied:
                    return "Access to the target is denied. Run Taskbar Groups as administrator to read it.";

                case ShortcutStatus.InvalidShortcut:
                    return "The .lnk could not be parsed by Windows, or its target was removed.";

                case ShortcutStatus.Disabled:
                    return "Disabled in the group editor, so launch-all skips it.";

                case ShortcutStatus.UriLauncher:
                    return "Verified when launched; the shell resolves it.";

                default:
                    return "Not enough information to tell.";
            }
        }

        public HealthCheckResult CheckDatabase()
        {
            var result = new HealthCheckResult { Name = "Database" };

            try
            {
                if (!_database.IsHealthy(out string openError))
                {
                    result.Status = HealthStatus.Problem;
                    result.Detail = "The database could not be opened: " + openError;
                    result.SuggestedAction = "Restore a backup, or move the database aside so a new one is created.";
                    return result;
                }

                if (!_database.CheckIntegrity(out string integrityError))
                {
                    result.Status = HealthStatus.Problem;
                    result.Detail = "SQLite reported a problem: " + integrityError;
                    result.SuggestedAction = "Use Repair database on this page, or restore the most recent backup.";
                    return result;
                }

                int groupCount = _groups.GetAll().Count;
                result.Status = HealthStatus.Healthy;
                result.Detail = groupCount + " group(s), schema version " + _database.SchemaVersion + ", integrity check passed.";
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Problem;
                result.Detail = ex.Message;
                _logger?.Log(SystemLogLevel.Error, "Diagnostics", "Database check failed", ex);
            }

            return result;
        }

        private HealthCheckResult CheckDataDirectory()
        {
            var result = new HealthCheckResult { Name = "Data directory" };

            try
            {
                if (_paths.IsWritable())
                {
                    result.Status = HealthStatus.Healthy;
                    result.Detail = _paths.BaseDirectory + " is writable.";
                }
                else
                {
                    result.Status = HealthStatus.Problem;
                    result.Detail = _paths.BaseDirectory + " is not writable.";
                    result.SuggestedAction = _isPortable
                        ? "Move the portable folder somewhere writable."
                        : "Install to a writable location, or use portable mode beside the executable.";
                }
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Problem;
                result.Detail = ex.Message;
            }

            return result;
        }

        private HealthCheckResult CheckIconCache()
        {
            var result = new HealthCheckResult { Name = "Icon cache" };

            try
            {
                if (Directory.Exists(_paths.IconsDirectory))
                {
                    string[] files = Directory.GetFiles(_paths.IconsDirectory, "*.png");
                    long total = 0;
                    foreach (string file in files)
                    {
                        try { total += new FileInfo(file).Length; } catch (Exception) { }
                    }

                    result.Status = HealthStatus.Healthy;
                    result.Detail = files.Length + " cached icon(s), " + (total / 1024) + " KB.";
                }
                else
                {
                    result.Status = HealthStatus.Warning;
                    result.Detail = "The icon cache folder does not exist yet. It is created on first use.";
                }
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Warning;
                result.Detail = ex.Message;
                result.SuggestedAction = "Use Rebuild icon cache.";
            }

            return result;
        }

        private HealthCheckResult CheckMonitors(MonitorQueryResult monitors)
        {
            var result = new HealthCheckResult { Name = "Monitors" };

            int count = monitors.Monitors.Count;

            if (count == 0)
            {
                result.Status = HealthStatus.Problem;
                result.Detail = "No monitors were detected.";
                result.SuggestedAction = "Check the display settings and that a session with a desktop is active.";
                return result;
            }

            var problems = new List<string>();
            var scales = new HashSet<double>();

            foreach (var monitor in monitors.Monitors)
            {
                scales.Add(Math.Round(monitor.ScaleFactor * 100));

                if (monitor.Bounds.Width <= 0 || monitor.Bounds.Height <= 0)
                    problems.Add(monitor.DeviceName + " reported an empty rectangle");
                if (!monitor.TaskbarDetected && !monitor.TaskbarAutoHide)
                    problems.Add(monitor.DeviceName + " has no detected taskbar");
            }

            result.Detail = count + " monitor(s): " + string.Join("; ", monitors.Monitors.Select(m => m.ToString()));

            if (scales.Count > 1)
            {
                result.Status = HealthStatus.Healthy;
                result.Detail += ". Mixed scaling is in use; each monitor is measured independently.";
            }
            else if (problems.Count > 0)
            {
                result.Status = HealthStatus.Warning;
                result.Detail += ". " + string.Join("; ", problems);
            }
            else
            {
                result.Status = HealthStatus.Healthy;
            }

            return result;
        }

        private HealthCheckResult CheckDpi()
        {
            var result = new HealthCheckResult { Name = "DPI awareness" };

            string awareness = DpiService.DescribeAwareness();

            if (awareness.IndexOf("Per monitor V2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.Status = HealthStatus.Healthy;
                result.Detail = awareness + ". Group popups stay sharp when moved between monitors.";
            }
            else if (awareness.IndexOf("unaware", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.Status = HealthStatus.Problem;
                result.Detail = awareness + ". Icons and the popup will look blurry at high scaling.";
                result.SuggestedAction = "Reinstall so the application manifest is applied.";
            }
            else
            {
                result.Status = HealthStatus.Warning;
                result.Detail = awareness + ".";
            }

            return result;
        }

        private HealthCheckResult CheckShortcutHealth(ShortcutValidationReport? shortcuts)
        {
            var result = new HealthCheckResult { Name = "Shortcuts" };

            if (shortcuts == null)
            {
                result.Status = HealthStatus.Unknown;
                result.Detail = "Validation did not run.";
                return result;
            }

            result.Detail = shortcuts.Total + " shortcut(s): "
                            + shortcuts.Valid + " ready, "
                            + shortcuts.Missing + " missing, "
                            + shortcuts.Broken + " broken, "
                            + shortcuts.Denied + " inaccessible, "
                            + shortcuts.Disabled + " disabled.";

            if (shortcuts.IsClean)
            {
                result.Status = HealthStatus.Healthy;
            }
            else if (shortcuts.Broken > 0 || shortcuts.Denied > 0)
            {
                result.Status = HealthStatus.Problem;
                result.SuggestedAction = "Open the group editor to repair or remove the flagged shortcuts.";
            }
            else
            {
                result.Status = HealthStatus.Warning;
                result.SuggestedAction = "A missing target is often an application that was moved or uninstalled.";
            }

            return result;
        }

        private HealthCheckResult CheckShortcutsFolder()
        {
            var result = new HealthCheckResult { Name = "Taskbar shortcuts" };

            try
            {
                string folder = _paths.LegacyShortcutsDirectory;

                if (!Directory.Exists(folder))
                {
                    result.Status = HealthStatus.Warning;
                    result.Detail = "The Shortcuts folder does not exist yet.";
                    result.SuggestedAction = "Saving any group creates it.";
                    return result;
                }

                string[] files = Directory.GetFiles(folder, "*.lnk");
                result.Detail = files.Length + " taskbar shortcut(s) in " + folder + ".";

                int groups = _groups.GetAll().Count;
                if (groups > 0 && files.Length == 0)
                {
                    result.Status = HealthStatus.Warning;
                    result.Detail += " No shortcuts exist, so no groups can be reached from the taskbar.";
                    result.SuggestedAction = "Open a group and use Repair taskbar shortcut.";
                }
                else
                {
                    result.Status = HealthStatus.Healthy;
                }
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Warning;
                result.Detail = ex.Message;
            }

            return result;
        }

        private HealthCheckResult CheckLegacyMigration()
        {
            var result = new HealthCheckResult { Name = "Legacy data" };

            try
            {
                if (!Directory.Exists(_paths.LegacyConfigDirectory))
                {
                    result.Status = HealthStatus.Healthy;
                    result.Detail = "No version 1.x data found, so nothing was migrated.";
                    return result;
                }

                int folders = Directory.GetDirectories(_paths.LegacyConfigDirectory).Length;

                if (folders == 0)
                {
                    result.Status = HealthStatus.Healthy;
                    result.Detail = "The legacy config folder exists but contains no groups.";
                    return result;
                }

                // Legacy data that is still present after a completed migration is
                // expected: it is kept deliberately so a user can roll back.
                result.Status = HealthStatus.Healthy;
                result.Detail = folders + " version 1.x group folder(s) are still present. "
                                + "They are kept on purpose and are not used; remove them yourself when you are satisfied.";
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Warning;
                result.Detail = ex.Message;
            }

            return result;
        }

        public string ExportReport(DiagnosticReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            try
            {
                Directory.CreateDirectory(_paths.LogsDirectory);

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string path = Path.Combine(_paths.LogsDirectory, "diagnostics-" + stamp + ".json");

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                };

                File.WriteAllText(path, JsonSerializer.Serialize(report, options));

                // A human-readable companion, because the JSON is verbose and most
                // users reporting a problem will not open it.
                string textPath = Path.Combine(_paths.LogsDirectory, "diagnostics-" + stamp + ".txt");
                File.WriteAllText(textPath, FormatAsText(report));

                return path;
            }
            catch (Exception ex)
            {
                _logger?.Log(SystemLogLevel.Error, "Diagnostics", "Report export failed", ex);
                return string.Empty;
            }
        }

        internal static string FormatAsText(DiagnosticReport report)
        {
            var builder = new StringBuilder();

            builder.AppendLine("Taskbar Groups diagnostics");
            builder.AppendLine("Generated: " + report.GeneratedAt.ToString("u"));
            builder.AppendLine("Version:   " + report.ApplicationVersion);
            builder.AppendLine("OS:        " + report.OsDescription + " (" + report.OsArchitecture + ")");
            builder.AppendLine("Process:   " + report.ProcessArchitecture);
            builder.AppendLine("Portable:  " + (report.IsPortable ? "yes" : "no"));
            builder.AppendLine("Data:      " + report.DataDirectory);
            builder.AppendLine("DPI:       " + report.DpiAwarenessContext);
            builder.AppendLine();

            builder.AppendLine("Monitors");
            foreach (var monitor in report.Monitors)
                builder.AppendLine("  " + monitor);
            builder.AppendLine();

            builder.AppendLine("Checks");
            foreach (HealthCheckResult check in report.Checks)
            {
                builder.AppendLine("  [" + check.Status + "] " + check.Name + ": " + check.Detail);
                if (!string.IsNullOrWhiteSpace(check.SuggestedAction))
                    builder.AppendLine("        -> " + check.SuggestedAction);
            }
            builder.AppendLine();

            if (report.Shortcuts != null && report.Shortcuts.Items.Count > 0)
            {
                builder.AppendLine("Shortcuts needing attention");
                foreach (ShortcutHealthReport item in report.Shortcuts.Items.Where(i => i.NeedsAttention))
                    builder.AppendLine("  [" + item.Status + "] " + item.Name + " - " + item.Detail);
            }

            if (report.LogPaths.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Logs");
                foreach (string path in report.LogPaths)
                    builder.AppendLine("  " + path);
            }

            return builder.ToString();
        }

        private static bool IsPerMonitorAware()
        {
            try
            {
                using Process current = Process.GetCurrentProcess();
                return current.MainWindowHandle == IntPtr.Zero
                    || DpiService.ScaleOfWindow(current.MainWindowHandle) > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static string ApplicationVersion()
        {
            try
            {
                System.Reflection.AssemblyName? name = System.Reflection.Assembly.GetEntryAssembly()?.GetName();
                return name?.Version?.ToString() ?? "unknown";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}