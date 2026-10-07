using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using TaskbarGroups.Core.Models;

namespace TaskbarGroups.Core.Interfaces
{
    /// <summary>
    /// Writes the pre-migration archive of the legacy data tree.
    /// </summary>
    /// <remarks>
    /// Lives in Core so the migration in
    /// <c>TaskbarGroups.Groups</c> can demand a backup without depending on the
    /// <c>TaskbarGroups.Data</c> implementation that performs it. The dependency
    /// arrow runs Groups -&gt; Core, never Data -&gt; Groups.
    /// </remarks>
    public interface IDataArchiver
    {
        /// <summary>
        /// Archives the legacy configuration tree and returns the archive path.
        /// Must throw rather than return a path when the archive is not written:
        /// the caller treats failure as "do not touch anything".
        /// </summary>
        string CreateLegacyBackup(string backupDirectory);
    }

    /// <summary>
    /// Starts whatever a <see cref="ShortcutItem"/> points at.
    /// </summary>
    /// <remarks>
    /// One launcher per <see cref="Enums.ShortcutType"/>. The resolver picks the
    /// implementation; nothing else decides how to start a target. This is the
    /// seam that stops the legacy "assume everything is an .exe" behaviour from
    /// spreading any further.
    /// </remarks>
    public interface IApplicationLauncher
    {
        /// <summary>Types this launcher claims. May be more than one (e.g. Bat and Cmd).</summary>
        IEnumerable<Enums.ShortcutType> SupportedTypes { get; }

        /// <summary>
        /// Whether this launcher wants the item. Called before validation so a
        /// launcher can accept items whose target cannot be probed offline.
        /// </summary>
        bool CanLaunch(ShortcutItem item);

        /// <summary>
        /// Starts the item. Must not throw for ordinary failures; return a failed
        /// <see cref="LaunchResult"/> instead.
        /// </summary>
        LaunchResult Launch(ShortcutItem item, LaunchContext context);
    }

    /// <summary>Ambient facts a launcher needs that are not on the item itself.</summary>
    public sealed class LaunchContext
    {
        public LaunchContext()
        {
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Directory the launcher should assume when the item does not specify one.</summary>
        public string DefaultWorkingDirectory { get; set; } = string.Empty;

        /// <summary>Set when script launching is disabled in settings.</summary>
        public bool ScriptsEnabled { get; set; } = true;

        /// <summary>Set when the user has confirmed the script warning.</summary>
        public bool ScriptWarningAcknowledged { get; set; }

        /// <summary>Environment variables expanded into item targets at import time.</summary>
        public IDictionary<string, string> EnvironmentVariables { get; }

        /// <summary>Builds the default context for this machine.</summary>
        public static LaunchContext CreateDefault()
        {
            var context = new LaunchContext
            {
                DefaultWorkingDirectory = AppContext.BaseDirectory
            };

            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                string key = entry.Key?.ToString() ?? string.Empty;
                if (key.Length == 0) continue;
                context.EnvironmentVariables[key] = entry.Value?.ToString() ?? string.Empty;
            }

            return context;
        }
    }

    /// <summary>
    /// Decides which launcher handles an item and drives it.
    /// </summary>
    public interface ILauncherResolver
    {
        /// <summary>The launcher that would handle this item, or null.</summary>
        IApplicationLauncher? Resolve(ShortcutItem item);

        /// <summary>Resolves and launches in one step.</summary>
        LaunchResult Launch(ShortcutItem item, LaunchContext context);

        /// <summary>
        /// Launches several items, continuing past failures. Used by Ctrl+Enter
        /// and by workspaces, which must not abort because one app is missing.
        /// </summary>
        LaunchBatchResult LaunchAll(IEnumerable<ShortcutItem> items, LaunchContext context, IProgress<LaunchResult>? progress = null);
    }

    /// <summary>Per-item outcome of a batch launch.</summary>
    public sealed class LaunchBatchResult
    {
        public LaunchBatchResult()
        {
            Results = new List<LaunchResult>();
        }

        public List<LaunchResult> Results { get; }

        public int Succeeded
        {
            get
            {
                int n = 0;
                foreach (var r in Results) { if (r.Success) n++; }
                return n;
            }
        }

        public int Failed => Results.Count - Succeeded;

        public bool AllSucceeded => Failed == 0;
    }

    /// <summary>Works out what a path or URI is, and validates it.</summary>
    public interface IShortcutResolver
    {
        /// <summary>Classifies a target without modifying anything.</summary>
        Enums.ShortcutType DetectType(string target);

        /// <summary>Turns a raw path/URI into a complete, validated item.</summary>
        ResolvedShortcut Resolve(string target, string? displayName = null);

        /// <summary>Follows a .lnk to its target.</summary>
        string? ResolveLinkTarget(string linkPath);
    }

    /// <summary>Fully resolved description of a shortcut target.</summary>
    public sealed class ResolvedShortcut
    {
        public ResolvedShortcut()
        {
            Type = Enums.ShortcutType.Unknown;
            Target = string.Empty;
            DisplayName = string.Empty;
            SuggestedWorkingDirectory = string.Empty;
            Warnings = new List<string>();
        }

        public Enums.ShortcutType Type { get; set; }

        /// <summary>Absolute, environment-expanded target.</summary>
        public string Target { get; set; }

        public string DisplayName { get; set; }

        public string SuggestedWorkingDirectory { get; set; }

        /// <summary>Non-fatal problems the editor shows as a warning badge.</summary>
        public List<string> Warnings { get; }

        public bool IsValid { get; set; }

        public bool Exists { get; set; }
    }

    /// <summary>Produces an icon for a shortcut, never failing.</summary>
    public interface IIconProvider
    {
        /// <summary>Types this provider handles.</summary>
        IEnumerable<Enums.ShortcutType> SupportedTypes { get; }

        /// <summary>Whether this provider wants to try. Cheap; no I/O.</summary>
        bool CanProvide(ShortcutItem item);

        /// <summary>
        /// Extracts the icon. Returns null when nothing could be produced; the
        /// cache substitutes the fallback and never propagates the failure.
        /// </summary>
        Bitmap? Extract(ShortcutItem item, IconRequest request);
    }

    /// <summary>What kind and size of icon is wanted.</summary>
    public sealed class IconRequest
    {
        /// <summary>Logical edge length in device-independent pixels.</summary>
        public int LogicalSize { get; set; } = 32;

        /// <summary>Monitor scale the icon will be drawn on.</summary>
        public double ScaleFactor { get; set; } = 1d;

        /// <summary>Physical edge length after scaling.</summary>
        public int PixelSize => Math.Max(16, (int)Math.Round(LogicalSize * ScaleFactor));
    }

    /// <summary>The application's single entry point for icons.</summary>
    public interface IIconCache
    {
        /// <summary>
        /// Returns an icon for the item, reading the cache when possible and
        /// extracting on a miss. Never returns null and never throws.
        /// </summary>
        Bitmap GetIcon(ShortcutItem item, IconRequest request);

        /// <summary>Content-addressed key for an item's icon inputs.</summary>
        string ComputeCacheKey(ShortcutItem item);

        /// <summary>Forces re-extraction for this item.</summary>
        void Invalidate(ShortcutItem item);

        /// <summary>Drops cached icons whose source no longer matches.</summary>
        int Repair(Func<ShortcutItem, bool> stillValid);

        /// <summary>Empties the cache. Used by the diagnostics "rebuild" action.</summary>
        void Clear();
    }

    /// <summary>Persistence for groups and their shortcuts.</summary>
    public interface IGroupRepository
    {
        List<Group> GetAll();

        Group? GetById(Guid id);

        /// <summary>Finds a group by display name, case-insensitively.</summary>
        Group? GetByName(string name);

        void Upsert(Group group);

        void Delete(Guid id);

        List<ShortcutItem> GetShortcuts(Guid groupId);

        void ReplaceShortcuts(Guid groupId, IEnumerable<ShortcutItem> items);
    }

    /// <summary>Persistence for workspaces, their items and saved layouts.</summary>
    public interface IWorkspaceRepository
    {
        List<Workspace> GetAll();

        Workspace? GetById(Guid id);

        Workspace? GetByName(string name);

        void Upsert(Workspace workspace);

        void Delete(Guid id);

        List<WindowPlacement> GetPlacements(Guid workspaceId);

        void ReplacePlacements(Guid workspaceId, IEnumerable<WindowPlacement> placements);
    }

    /// <summary>Creates, launches and restores workspaces.</summary>
    public interface IWorkspaceService
    {
        List<Workspace> GetWorkspaces();

        Workspace CreateWorkspace(string name);

        Workspace DuplicateWorkspace(Guid id);

        void DeleteWorkspace(Guid id);

        /// <summary>
        /// Launches every item and then applies the recorded window layout.
        /// </summary>
        Task<WorkspaceLaunchResult> LaunchWorkspaceAsync(Workspace workspace, IProgress<LaunchResult>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>Captures the current on-screen windows as this workspace's layout.</summary>
        WindowPlacement[] CaptureCurrentLayout();
    }

    /// <summary>Result of launching a workspace.</summary>
    public sealed class WorkspaceLaunchResult
    {
        public WorkspaceLaunchResult()
        {
            LaunchResults = new List<LaunchResult>();
        }

        public bool Success { get; set; }

        public List<LaunchResult> LaunchResults { get; }

        public int WindowsPlaced { get; set; }

        public int WindowsNotFound { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    /// <summary>Everything the UI needs to know about monitors and popups.</summary>
    public interface IMonitorService
    {
        /// <summary>Re-reads the display configuration. Call after a display change.</summary>
        MonitorQueryResult GetMonitors();

        /// <summary>The monitor containing a physical point.</summary>
        MonitorInfo? GetMonitorAt(Point physicalPoint);

        /// <summary>Finds a monitor by its Win32 device name.</summary>
        MonitorInfo? GetMonitorByDeviceName(string? deviceName);

        /// <summary>The primary monitor, or the first available one.</summary>
        MonitorInfo? GetPrimary();

        /// <summary>
        /// Computes where a popup of the given logical size should appear for an
        /// anchor point, clamped to the chosen monitor's working area.
        /// </summary>
        PopupPlacement CalculatePopupPlacement(PopupPlacementRequest request);
    }

    /// <summary>Input to <see cref="IMonitorService.CalculatePopupPlacement"/>.</summary>
    public sealed class PopupPlacementRequest
    {
        public PopupPlacementRequest()
        {
            AnchorPoint = Point.Empty;
            DesiredSize = new Size(320, 180);
            MonitorPreference = Enums.MonitorPreference.FollowCursor;
            PreferredMonitorDeviceName = string.Empty;
            Margin = 10;
        }

        /// <summary>Physical point the popup is being opened from (cursor or window corner).</summary>
        public Point AnchorPoint { get; set; }

        /// <summary>Desired popup size in logical pixels.</summary>
        public Size DesiredSize { get; set; }

        public Enums.MonitorPreference MonitorPreference { get; set; }

        public string PreferredMonitorDeviceName { get; set; }

        /// <summary>Distance kept from the work-area edge, in logical pixels.</summary>
        public int Margin { get; set; }

        /// <summary>
        /// Where the anchor sits relative to the taskbar. When the anchor is on a
        /// docked taskbar the popup opens inwards from that edge.
        /// </summary>
        public bool AnchorOnTaskbar { get; set; }

        /// <summary>Minimum size the popup may shrink to before it is allowed to clip.</summary>
        public Size MinimumSize { get; set; } = new Size(160, 96);
    }

    /// <summary>Taskbar inspection and shortcut maintenance.</summary>
    public interface ITaskbarService
    {
        /// <summary>Finds the taskbar rectangle on the monitor containing a point.</summary>
        Rectangle? GetTaskbarRectangle(Point physicalPoint);

        /// <summary>True when a point lies on a docked taskbar.</summary>
        bool IsOnTaskbar(Point physicalPoint);

        /// <summary>Windows whose class name is the shell taskbar, for later verification.</summary>
        bool IsTaskbarPresent { get; }

        /// <summary>Path of the .lnk that launches a group popup, if it exists.</summary>
        string? FindGroupShortcut(string groupName, bool legacyUnderscoreName);

        /// <summary>
        /// Rewrites a group's taskbar shortcut. The AppUserModelID scheme
        /// <c>tjackenpacken.taskbarGroup.menu.&lt;name&gt;</c> must be preserved,
        /// because that is what already-pinned entries depend on.
        /// </summary>
        bool CreateGroupShortcut(string groupName, string exePath, string iconPath, out string error);

        /// <summary>Removes a group's taskbar shortcut.</summary>
        bool DeleteGroupShortcut(string groupName, bool legacyUnderscoreName, out string error);
    }

    /// <summary>Moves, sizes and shows top-level windows.</summary>
    public interface IWindowManager
    {
        /// <summary>Finds a top-level window by executable name or title substring.</summary>
        IntPtr FindWindow(string executableOrTitle, bool matchTitle = false);

        /// <summary>
        /// Applies a placement, converting the stored device-independent pixels
        /// into the physical coordinates the window manager expects.
        /// </summary>
        bool ApplyPlacement(IntPtr window, WindowPlacement placement, double scaleFactor);

        /// <summary>Lists the visible top-level windows of the current session.</summary>
        IReadOnlyList<WindowPlacement> ListVisibleWindows();
    }

    /// <summary>Finds applications installed on this machine.</summary>
    public interface IApplicationDiscoveryService
    {
        Task<IReadOnlyList<DiscoveredApplication>> DiscoverAsync(DiscoveryOptions options, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    }

    /// <summary>Writes and restores backups.</summary>
    public interface IBackupService
    {
        /// <summary>Writes a backup and returns its path.</summary>
        string CreateBackup(string? reason = null);

        /// <summary>Validates an archive without writing anything.</summary>
        BackupValidationResult Validate(string archivePath);

        /// <summary>
        /// Restores from a backup. Any current database is copied aside first, so
        /// a bad restore is recoverable.
        /// </summary>
        RestoreResult Restore(string archivePath);

        /// <summary>Removes old backups beyond the configured rotation count.</summary>
        int PruneOldBackups(int keep);
    }

    /// <summary>Result of validating a backup archive.</summary>
    public sealed class BackupValidationResult
    {
        public BackupValidationResult()
        {
            Problems = new List<string>();
        }

        public bool IsValid { get; set; }

        public bool HasDatabase { get; set; }

        public bool HasJsonExport { get; set; }

        public int IconCount { get; set; }

        public string CreatedAt { get; set; } = string.Empty;

        public string ApplicationVersion { get; set; } = string.Empty;

        public List<string> Problems { get; }
    }

    /// <summary>Result of restoring a backup.</summary>
    public sealed class RestoreResult
    {
        public RestoreResult()
        {
            Problems = new List<string>();
        }

        public bool Success { get; set; }

        /// <summary>Deliberate choices and informational notes, kept apart from problems.</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>Where the pre-restore database was moved, when one existed.</summary>
        public string PreviousDatabaseBackup { get; set; } = string.Empty;

        public List<string> Problems { get; }
    }

    /// <summary>Self-checks and report generation.</summary>
    public interface IDiagnosticsService
    {
        Task<DiagnosticReport> BuildReportAsync(CancellationToken cancellationToken = default);

        Task<ShortcutValidationReport> ValidateShortcutsAsync(CancellationToken cancellationToken = default);

        /// <summary>Verifies the database is readable, consistent and migratable.</summary>
        HealthCheckResult CheckDatabase();

        /// <summary>Writes a diagnostics report to disk and returns the path.</summary>
        string ExportReport(DiagnosticReport report);
    }

    /// <summary>Update checking against a GitHub releases feed.</summary>
    public interface IUpdateService
    {
        Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

        string CurrentVersion { get; }
    }

    /// <summary>Outcome of an update check.</summary>
    public sealed class UpdateCheckResult
    {
        public UpdateCheckResult()
        {
            Message = string.Empty;
        }

        public bool CheckSucceeded { get; set; }

        public bool UpdateAvailable { get; set; }

        public string CurrentVersion { get; set; } = string.Empty;

        public string LatestVersion { get; set; } = string.Empty;

        public string ReleaseUrl { get; set; } = string.Empty;

        public string Message { get; set; }
    }

    /// <summary>Structured logging abstraction, so Core does not depend on a logging package.</summary>
    public interface IAppLogger
    {
        bool IsEnabled(SystemLogLevel level);

        void Log(SystemLogLevel level, string subsystem, string message, Exception? exception = null);
    }

    /// <summary>Log severities. Deliberately mirrors the usual set without taking a dependency on it.</summary>
    public enum SystemLogLevel
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5
    }

    /// <summary>Reads and writes the settings document.</summary>
    public interface ISettingsService
    {
        AppSettings Current { get; }

        void Save(AppSettings settings);

        ThemeDefinition GetTheme(Enums.GroupTheme theme);

        void SaveTheme(ThemeDefinition theme);
    }

    /// <summary>Reads and writes icon metadata rows in the database.</summary>
    public interface IIconRepository
    {
        void Upsert(string cacheKey, string filePath, string sourceFingerprint, int pixelSize, long byteLength, DateTimeOffset createdAt);

        IconRecord? Get(string cacheKey);

        IEnumerable<IconRecord> GetAll();

        int Delete(string cacheKey);

        int DeleteAll();
    }

    /// <summary>Metadata for one cached icon file.</summary>
    public sealed class IconRecord
    {
        public string CacheKey { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;

        /// <summary>Hash of the source the icon was produced from; changes invalidate it.</summary>
        public string SourceFingerprint { get; set; } = string.Empty;

        public int PixelSize { get; set; }

        public long ByteLength { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}