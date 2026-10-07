using System;
using System.Collections.Generic;
using System.IO;
using TaskbarGroups.App.Configuration;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Data.Backup;
using TaskbarGroups.Data.Json;
using TaskbarGroups.Data.Repositories;
using TaskbarGroups.Data.Storage;
using TaskbarGroups.Discovery.ApplicationDiscovery;
using TaskbarGroups.Discovery.BrowserDiscovery;
using TaskbarGroups.Discovery.SteamDiscovery;
using TaskbarGroups.Diagnostics.HealthChecks;
using TaskbarGroups.Groups.GroupMigration;
using TaskbarGroups.Groups.SmartGroups;
using TaskbarGroups.Icons.Cache;
using TaskbarGroups.Launchers;
using TaskbarGroups.Updates.UpdateService;
using TaskbarGroups.Windows.Monitor;
using TaskbarGroups.Windows.Startup;
using TaskbarGroups.Windows.Taskbar;
using TaskbarGroups.Windows.WindowManagement;

namespace TaskbarGroups.App.Services
{
    /// <summary>
    /// The application's object graph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Composition root, built by hand rather than with a container. The graph has
    /// one owner per subsystem and no circular references, so a container would add
    /// a package and a layer of indirection without removing any wiring. What
    /// matters is the order: paths first, then logging, then the database, then
    /// everything that depends on the database.
    /// </para>
    /// <para>
    /// Construction order also handles two things the legacy entry point did not.
    /// The shell-link reader is registered with Core here, so shortcut validation
    /// can resolve a .lnk without Core depending on COM. And a failed legacy
    /// migration leaves the original files untouched and the application usable,
    /// because the migrator is given the archiver rather than doing it itself.
    /// </para>
    /// </remarks>
    public sealed class ServiceContainer : IDisposable
    {
        private bool _disposed;

        private ServiceContainer()
        {
        }

        public DataPaths Paths { get; private set; } = null!;

        public PortableMode PortableMode { get; private set; }

        public bool IsPortable => PortableMode != PortableMode.LocalApplicationData;

        public IAppLogger Logger { get; private set; } = null!;

        public AppSettings Settings { get; private set; } = null!;

        public Database Database { get; private set; } = null!;

        public IGroupRepository GroupRepository { get; private set; } = null!;

        public IWorkspaceRepository WorkspaceRepository { get; private set; } = null!;

        public IIconRepository IconRepository { get; private set; } = null!;

        public MigrationHistoryRepository MigrationHistory { get; private set; } = null!;

        public SettingsRepository SettingsRepository { get; private set; } = null!;

        public IMonitorService Monitors { get; private set; } = null!;

        public MonitorService MonitorService { get; private set; } = null!;

        public ITaskbarService Taskbar { get; private set; } = null!;

        public IWindowManager Windows { get; private set; } = null!;

        public IShortcutResolver Shortcuts { get; private set; } = null!;

        public ILauncherResolver Launchers { get; private set; } = null!;

        public IIconCache Icons { get; private set; } = null!;

        public IBackupService Backups { get; private set; } = null!;

        public ExportService Exporter { get; private set; } = null!;

        public IDiagnosticsService Diagnostics { get; private set; } = null!;

        public IUpdateService Updates { get; private set; } = null!;

        public IApplicationDiscoveryService Discovery { get; private set; } = null!;

        public BrowserLocator Browsers { get; private set; } = null!;

        public StartupService Startup { get; private set; } = null!;

        public SmartGroupService SmartGroups { get; private set; } = null!;

        public MigrationReport? MigrationReport { get; private set; }

        public string ExecutablePath { get; private set; } = string.Empty;

        public static ServiceContainer Create()
        {
            var container = new ServiceContainer();

            container.Paths = DataPathResolver.Resolve(out PortableMode mode);
            container.PortableMode = mode;
            container.ExecutablePath = ResolveExecutablePath();

            container.Paths.EnsureCreated();

            container.Settings = LoadSettings(container);
            container.Logger = CreateLogger(container);

            // UI language applies to every form; set it before any form is built.
            TaskbarGroups.App.Configuration.Localization.Current = container.Settings.Language;

            // Core needs to be able to resolve a .lnk for validation. Registering
            // the delegate here is what keeps Core free of COM interop.
            Core.Validation.LinkResolver.Register(TaskbarGroups.Windows.Shell.ShellLinkReader.GetTargetPath);

            container.Database = new Database(container.Paths.DatabaseFile);
            SchemaMigrator.Initialize(container.Database, container.Logger);

            container.SettingsRepository = new SettingsRepository(container.Database);
            container.GroupRepository = new GroupRepository(container.Database);
            container.WorkspaceRepository = new WorkspaceRepository(container.Database);
            container.IconRepository = new IconRepository(container.Database);
            container.MigrationHistory = new MigrationHistoryRepository(container.Database);

            container.MonitorService = new MonitorService();
            container.Monitors = container.MonitorService;
            container.Taskbar = new TaskbarService(container.MonitorService, container.Paths.LegacyShortcutsDirectory, container.ExecutablePath);
            container.Windows = new WindowManager();

            container.Browsers = new BrowserLocator(container.Logger);
            container.Shortcuts = new Launchers.Detection.ShortcutResolver();
            container.Launchers = LauncherResolver.CreateDefault(
                container.Browsers.FindBrowserFor, container.Logger, container.Shortcuts);

            container.Icons = IconCache.CreateDefault(
                container.Paths.IconsDirectory,
                container.IconRepository,
                container.Logger,
                container.Browsers.FindIcon,
                new SteamLibraryReader(container.Logger).FindSteamRoot());

            container.Exporter = new ExportService();
            container.Backups = new BackupService(
                container.Database, container.Paths,
                container.GroupRepository, container.WorkspaceRepository,
                container.Exporter, container.Logger);

            container.Diagnostics = new DiagnosticsService(
                container.Database, container.Paths,
                container.GroupRepository, container.Icons,
                container.MonitorService, container.Settings,
                container.IsPortable, container.Logger);

            container.Updates = new UpdateService(container.Settings, container.Logger);

            container.Discovery = new ApplicationDiscoveryService(container.Browsers, container.Logger);
            container.SmartGroups = new SmartGroupService(
                container.GroupRepository, container.Shortcuts, container.Logger);

            container.Startup = new StartupService(container.ExecutablePath);

            container.RunMigration();

            container.Logger.Log(SystemLogLevel.Information, "App",
                "Started in " + container.PortableMode + " mode; data at " + container.Paths.BaseDirectory);

            return container;
        }

        /// <summary>
        /// Imports legacy data when it is present and has not been imported yet.
        /// </summary>
        /// <remarks>
        /// Failures are logged and reported on the diagnostics page; they never stop
        /// the application starting. A user whose migration failed still has a
        /// working Taskbar Groups, and their original files are still on disk.
        /// </remarks>
        private void RunMigration()
        {
            try
            {
                var reader = new Data.LegacyXml.LegacyCategoryReader(Paths.LegacyConfigDirectory);
                var migrator = new LegacyMigrator(
                    Database, GroupRepository, reader, MigrationHistory,
                    (BackupService)Backups, Logger);

                if (!migrator.ShouldRun()) return;

                MigrationReport = migrator.Migrate(Paths.BackupsDirectory);
                Logger.Log(SystemLogLevel.Information, "Migration",
                    "Legacy migration finished: success=" + MigrationReport.Succeeded
                    + ", groups=" + MigrationReport.GroupsImportedCount);
            }
            catch (Exception ex)
            {
                Logger.Log(SystemLogLevel.Error, "Migration",
                    "Legacy migration failed; the legacy data was left untouched", ex);
            }
        }

        private static AppSettings LoadSettings(ServiceContainer container)
        {
            try
            {
                var repository = new SettingsRepository(container.Database);
                AppSettings settings = repository.Load(SettingsRepository.AppSettingsKey, () => new AppSettings());

                // Reconcile the startup setting with the actual registry state so
                // the two can never disagree in the UI.
                try
                {
                    var startup = new StartupService(container.ExecutablePath);
                    bool registered = startup.IsEnabled();
                    if (registered != settings.StartWithWindows)
                    {
                        settings.StartWithWindows = registered;
                        repository.Save(SettingsRepository.AppSettingsKey, settings);
                    }
                }
                catch (Exception)
                {
                    // Leave the stored value alone if the registry is unreadable.
                }

                return settings;
            }
            catch (Exception)
            {
                return new AppSettings();
            }
        }

        private static IAppLogger CreateLogger(ServiceContainer container)
        {
            SystemLogLevel level = SystemLogLevel.Information;

            try
            {
                switch (container.Settings.LogLevel?.ToLowerInvariant())
                {
                    case "trace": level = SystemLogLevel.Trace; break;
                    case "debug": level = SystemLogLevel.Debug; break;
                    case "warning": level = SystemLogLevel.Warning; break;
                    case "error": level = SystemLogLevel.Error; break;
                    case "critical": level = SystemLogLevel.Critical; break;
                    default: level = SystemLogLevel.Information; break;
                }
            }
            catch (Exception)
            {
            }

            return new FileLogger(container.Paths.LogsDirectory, level, container.Settings.LoggingEnabled);
        }

        public void SaveSettings(AppSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));

            try
            {
                SettingsRepository.Save(SettingsRepository.AppSettingsKey, Settings);
                Logger.Log(SystemLogLevel.Information, "App", "Settings saved");
            }
            catch (Exception ex)
            {
                Logger.Log(SystemLogLevel.Error, "App", "Settings could not be saved", ex);
            }
        }

        /// <summary>
        /// A launch context built from the current settings.
        /// </summary>
        /// <remarks>
        /// Rebuilt per call rather than cached, because the script setting can be
        /// changed while the application is running and a cached context would keep
        /// honouring the old value.
        /// </remarks>
        public LaunchContext CreateLaunchContext()
        {
            LaunchContext context = LaunchContext.CreateDefault();
            context.ScriptsEnabled = Settings.EnableScriptLaunching;
            context.ScriptWarningAcknowledged = !Settings.ShowScriptWarning;
            return context;
        }

        private static string ResolveExecutablePath()
        {
            try
            {
                string? path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path)) return path!;
            }
            catch (Exception)
            {
            }

            try
            {
                return System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>Creates a group whose taskbar shortcut is written on save.</summary>
        public Group CreateGroup(string name)
        {
            var group = new Group
            {
                Name = name,
                Width = Settings.GroupWidth,
                Theme = Settings.GroupTheme,
                Opacity = Settings.GroupOpacity,
                CornerRadius = Settings.GroupCornerRadius,
                ShadowEnabled = Settings.GroupShadowEnabled,
                BlurEnabled = Settings.GroupBlurEnabled,
                AnimationEnabled = Settings.GroupAnimationEnabled
            };

            GroupRepository.Upsert(group);
            Logger.Log(SystemLogLevel.Information, "App", "Created group " + group.Name);
            return group;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { (Icons as IDisposable)?.Dispose(); } catch (Exception) { }
            try { (Logger as IDisposable)?.Dispose(); } catch (Exception) { }
            try { (Updates as IDisposable)?.Dispose(); } catch (Exception) { }
            try { Database?.Dispose(); } catch (Exception) { }
        }
    }
}