using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Data.Backup;
using TaskbarGroups.Data.Json;
using TaskbarGroups.Data.Repositories;
using TaskbarGroups.Data.Storage;
using TaskbarGroups.Diagnostics.HealthChecks;
using TaskbarGroups.Groups.GroupMigration;
using TaskbarGroups.Data.LegacyXml;
using TaskbarGroups.Windows.Monitor;
using Xunit;

namespace TaskbarGroups.IntegrationTests
{
    /// <summary>
    /// A full 2.0 stack rooted in a temp directory: database, repositories,
    /// backup service and export service, wired the same way the application
    /// wires them in <c>ServiceContainer</c>.
    /// </summary>
    public sealed class IntegrationStack : IDisposable
    {
        private readonly string _root;

        public IntegrationStack()
        {
            _root = Path.Combine(Path.GetTempPath(),
                "taskbargroups-integration-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_root);

            Paths = new DataPaths(_root);
            Paths.EnsureCreated();

            Database = new Database(Paths.DatabaseFile);
            SchemaMigrator.Initialize(Database);

            Groups = new GroupRepository(Database);
            Workspaces = new WorkspaceRepository(Database);
            History = new MigrationHistoryRepository(Database);
            Exporter = new ExportService();
            Backups = new BackupService(Database, Paths, Groups, Workspaces, Exporter);
        }

        public string Root => _root;

        public DataPaths Paths { get; }

        public Database Database { get; }

        public GroupRepository Groups { get; }

        public WorkspaceRepository Workspaces { get; }

        public MigrationHistoryRepository History { get; }

        public ExportService Exporter { get; }

        public BackupService Backups { get; }

        public LegacyMigrator CreateMigrator()
        {
            return new LegacyMigrator(
                Database, Groups,
                new LegacyCategoryReader(Paths.LegacyConfigDirectory),
                History, Backups);
        }

        public void Dispose()
        {
            try { Database.Dispose(); } catch (Exception) { }
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (Exception) { }
        }
    }

    /// <summary>End-to-end across modules: legacy XML → migration → backup → restore.</summary>
    public class MigrationToBackupTests
    {
        [Fact]
        public void A_full_legacy_install_survives_migration_backup_and_restore()
        {
            using var stack = new IntegrationStack();

            // Seed a 1.x layout.
            string configRoot = stack.Paths.LegacyConfigDirectory;
            Directory.CreateDirectory(Path.Combine(configRoot, "Work"));
            File.WriteAllText(Path.Combine(configRoot, "Work", "ObjectData.xml"),
                "<?xml version=\"1.0\"?><Category><Name>Work</Name><allowOpenAll>true</allowOpenAll>" +
                "<Width>6</Width><Opacity>25</Opacity>" +
                "<ShortcutList><ProgramShortcut><FilePath>C:\\Windows\\System32\\notepad.exe</FilePath>" +
                "<isWindowsApp>false</isWindowsApp><name>Notepad</name>" +
                "<Arguments>readme.txt</Arguments><WorkingDirectory>C:\\Users</WorkingDirectory>" +
                "</ProgramShortcut></ShortcutList></Category>");

            // Migrate.
            MigrationReport report = stack.CreateMigrator().Migrate(stack.Paths.BackupsDirectory);

            Assert.True(report.Succeeded);
            Assert.Single(stack.Groups.GetAll());

            Group group = stack.Groups.GetByName("Work")!;
            Assert.Equal(6, group.Width);
            Assert.Equal(25d, group.Opacity);
            Assert.Single(stack.Groups.GetShortcuts(group.Id));

            // Backup.
            string backup = stack.Backups.CreateBackup("integration");

            Assert.True(File.Exists(backup));

            // Wipe and restore.
            stack.Groups.Delete(group.Id);
            Assert.Empty(stack.Groups.GetAll());

            RestoreResult restored = stack.Backups.Restore(backup);

            Assert.True(restored.Success, string.Join("; ", restored.Problems));
            Group? back = stack.Groups.GetByName("Work");
            Assert.NotNull(back);
            Assert.Single(stack.Groups.GetShortcuts(back!.Id));
            Assert.Equal(ShortcutType.Exe, stack.Groups.GetShortcuts(back.Id)[0].Type);
        }

        [Fact]
        public void A_second_backup_after_restore_contains_the_restored_data()
        {
            using var stack = new IntegrationStack();

            Group group = new Group { Name = "Office", Width = 4 };
            stack.Groups.Upsert(group);

            string first = stack.Backups.CreateBackup("first");
            stack.Groups.Delete(group.Id);
            stack.Backups.Restore(first);

            string second = stack.Backups.CreateBackup("second");

            BackupValidationResult validation = stack.Backups.Validate(second);
            Assert.True(validation.IsValid, string.Join("; ", validation.Problems));

            // The fresh backup must be enough on its own to rebuild the state.
            stack.Groups.Delete(stack.Groups.GetAll().Single().Id);
            RestoreResult restored = stack.Backups.Restore(second);

            Assert.True(restored.Success, string.Join("; ", restored.Problems));
            Assert.NotNull(stack.Groups.GetByName("Office"));
        }
    }

    /// <summary>Settings and search repositories, together.</summary>
    public class RepositoryCoordinationTests
    {
        [Fact]
        public void Settings_survive_a_restart_cycle()
        {
            using var stack = new IntegrationStack();
            var settings = new SettingsRepository(stack.Database);

            AppSettings saved = new AppSettings { GroupWidth = 8, GroupCornerRadius = 14 };
            settings.Save(SettingsRepository.AppSettingsKey, saved);

            // Simulate a restart: new repository over the same file.
            var reloaded = new SettingsRepository(stack.Database);
            AppSettings loaded = reloaded.Load(SettingsRepository.AppSettingsKey, () => new AppSettings());

            Assert.Equal(8, loaded.GroupWidth);
            Assert.Equal(14, loaded.GroupCornerRadius);
        }

        [Fact]
        public void Save_then_load_round_trips_a_whole_settings_document()
        {
            using var stack = new IntegrationStack();
            var settings = new SettingsRepository(stack.Database);

            AppSettings original = new AppSettings
            {
                GroupTheme = GroupTheme.Mica,
                GroupWidth = 7,
                StartInTray = false,
                StartWithWindows = true,
                LogLevel = "Debug"
            };

            settings.Save(SettingsRepository.AppSettingsKey, original);
            AppSettings loaded = settings.Load(SettingsRepository.AppSettingsKey, () => new AppSettings());

            Assert.Equal(GroupTheme.Mica, loaded.GroupTheme);
            Assert.Equal(7, loaded.GroupWidth);
            Assert.False(loaded.StartInTray);
            Assert.True(loaded.StartWithWindows);
            Assert.Equal("Debug", loaded.LogLevel);
        }

        [Fact]
        public void An_icon_record_is_written_and_found_by_cache_key()
        {
            using var stack = new IntegrationStack();
            var icons = new IconRepository(stack.Database);

            icons.Upsert("key-abc", @"C:\icons\app.png", "sha256:deadbeef", 32, 2048, DateTimeOffset.UtcNow);

            IconRecord? record = icons.Get("key-abc");

            Assert.NotNull(record);
            Assert.Equal(@"C:\icons\app.png", record!.FilePath);
            Assert.Equal(2048, record.ByteLength);
        }

        [Fact]
        public void Deleting_all_icon_records_leaves_none()
        {
            using var stack = new IntegrationStack();
            var icons = new IconRepository(stack.Database);

            icons.Upsert("k1", @"C:\a.png", "fp1", 32, 100, DateTimeOffset.UtcNow);
            icons.Upsert("k2", @"C:\b.png", "fp2", 32, 100, DateTimeOffset.UtcNow);

            icons.DeleteAll();

            Assert.Empty(icons.GetAll());
        }
    }

    /// <summary>A throwaway icon cache that returns a 1×1 bitmap without touching the shell.</summary>
    internal sealed class StubIconCache : IIconCache
    {
        public Bitmap GetIcon(ShortcutItem item, IconRequest request) => new Bitmap(1, 1);

        public string ComputeCacheKey(ShortcutItem item) => "stub";

        public void Invalidate(ShortcutItem item) { }

        public int Repair(Func<ShortcutItem, bool> stillValid) => 0;

        public void Clear() { }
    }

    /// <summary>The diagnostics service against a real (small) stack.</summary>
    public class DiagnosticsIntegrationTests
    {
        [Fact]
        public async System.Threading.Tasks.Task A_report_is_built_for_a_minimal_install()
        {
            using var stack = new IntegrationStack();

            var icons = new IconRepository(stack.Database);
            var iconCache = new StubIconCache();

            var service = new DiagnosticsService(
                stack.Database, stack.Paths, stack.Groups,
                iconCache, new MonitorService(), new AppSettings(),
                isPortable: true, logger: null);

            DiagnosticReport report = await service.BuildReportAsync();

            Assert.NotNull(report);
            Assert.Equal(stack.Paths.BaseDirectory, report.DataDirectory);
            Assert.NotEmpty(report.OsDescription);
            Assert.NotEmpty(report.Checks);
        }
    }
}