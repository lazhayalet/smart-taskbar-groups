using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Data.Backup;
using TaskbarGroups.Data.Json;
using TaskbarGroups.Data.LegacyXml;
using TaskbarGroups.Data.Repositories;
using TaskbarGroups.Data.Storage;
using TaskbarGroups.Groups.GroupMigration;
using Xunit;

namespace TaskbarGroups.MigrationTests
{
    /// <summary>One entry in a 1.x ObjectData.xml.</summary>
    internal sealed class LegacyShortcutSpec
    {
        public LegacyShortcutSpec(string name, string path, string args = "", string workingDir = "", bool isWindowsApp = false)
        {
            Name = name;
            Path = path;
            Args = args;
            WorkingDir = workingDir;
            IsWindowsApp = isWindowsApp;
        }

        public string Name { get; }

        public string Path { get; }

        public string Args { get; }

        public string WorkingDir { get; }

        public bool IsWindowsApp { get; }
    }

    /// <summary>
    /// A throwaway install of a 1.x layout: a data folder with a <c>config/</c>
    /// tree of group folders, each holding an ObjectData.xml.
    /// </summary>
    internal sealed class LegacyInstall : IDisposable
    {
        private string _root;

        public LegacyInstall(string? configDirectory = null)
        {
            if (configDirectory != null)
            {
                // Placed inside an existing data directory, so the legacy tree
                // really is at the path DataPaths.LegacyConfigDirectory reports.
                _root = Path.GetDirectoryName(configDirectory)!;
                ConfigDirectory = configDirectory;
            }
            else
            {
                _root = Path.Combine(Path.GetTempPath(),
                    "taskbargroups-legacy-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                ConfigDirectory = Path.Combine(_root, "config");
            }

            Directory.CreateDirectory(ConfigDirectory);
        }

        public string Root => _root;

        public string ConfigDirectory { get; private set; }

        /// <summary>
        /// Moves the whole legacy tree under <paramref name="configDirectory"/>.
        /// </summary>
        /// <remarks>
        /// The archiver only reads <c>DataPaths.LegacyConfigDirectory</c>, so the
        /// fixture has to sit there rather than in a directory of its own; otherwise
        /// a test that seeds legacy data and then checks the backup would pass on
        /// the importer while failing on the archiver.
        /// </remarks>
        public void RelocateTo(string configDirectory)
        {
            if (Directory.Exists(ConfigDirectory))
                CopyTree(ConfigDirectory, configDirectory);

            ConfigDirectory = configDirectory;
            _root = Path.GetDirectoryName(configDirectory)!;
        }

        private static void CopyTree(string from, string to)
        {
            foreach (string directory in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(directory.Replace(from, to));

            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string target = file.Replace(from, to);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }

        /// <summary>Writes a group folder and returns its full path.</summary>
        public string AddGroup(string folderName, string xml)
        {
            string folder = Path.Combine(ConfigDirectory, folderName);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "ObjectData.xml"), xml, Encoding.UTF8);
            return folder;
        }

        public string AddGroupIcon(string folderName, string content)
        {
            string folder = Path.Combine(ConfigDirectory, folderName);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "GroupIcon.ico"), content, Encoding.UTF8);
            return folder;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Builds an ObjectData.xml in the exact 1.x shape.</summary>
        public static string CategoryXml(string name, params LegacyShortcutSpec[] shortcuts)
        {
            return FullCategoryXml(name, 5, 10d, true, "#1f1f1f", shortcuts);
        }

        /// <summary>
        /// The full 1.x shape, with every group-level property explicit. Separate from
        /// the overload above because C# will not let a <c>params</c> array follow
        /// named arguments.
        /// </summary>
        public static string FullCategoryXml(
            string name,
            int width,
            double opacity,
            bool allowOpenAll,
            string? colorString,
            params LegacyShortcutSpec[] shortcuts)
        {
            var builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.Append("<Category>");
            builder.Append("<Name>").Append(name).Append("</Name>");
            if (colorString != null)
                builder.Append("<ColorString>").Append(colorString).Append("</ColorString>");
            builder.Append("<allowOpenAll>").Append(allowOpenAll ? "true" : "false").Append("</allowOpenAll>");
            builder.Append("<Width>").Append(width).Append("</Width>");
            builder.Append("<Opacity>").Append(opacity.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("</Opacity>");
            builder.Append("<ShortcutList>");

            foreach (LegacyShortcutSpec spec in shortcuts)
            {
                builder.Append("<ProgramShortcut>");
                builder.Append("<FilePath>").Append(Escape(spec.Path)).Append("</FilePath>");
                builder.Append("<isWindowsApp>").Append(spec.IsWindowsApp ? "true" : "false").Append("</isWindowsApp>");
                builder.Append("<name>").Append(Escape(spec.Name)).Append("</name>");
                builder.Append("<Arguments>").Append(Escape(spec.Args)).Append("</Arguments>");
                builder.Append("<WorkingDirectory>").Append(Escape(spec.WorkingDir)).Append("</WorkingDirectory>");
                builder.Append("</ProgramShortcut>");
            }

            builder.Append("</ShortcutList>");
            builder.Append("</Category>");
            return builder.ToString();
        }

        private static string Escape(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                        .Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }

    /// <summary>Wiring shared by the migration tests.</summary>
    internal sealed class MigrationHarness : IDisposable
    {
        private readonly string _root;

        public MigrationHarness(LegacyInstall legacy)
        {
            _root = Path.Combine(Path.GetTempPath(),
                "taskbargroups-migrate-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_root);

            Legacy = legacy;
            Paths = new DataPaths(_root);
            Paths.EnsureCreated();

            // The archiver reads DataPaths.LegacyConfigDirectory, so the fixture
            // must live there rather than in a directory of its own.
            legacy.RelocateTo(Paths.LegacyConfigDirectory);

            Database = new Database(Paths.DatabaseFile);
            SchemaMigrator.Initialize(Database);

            Groups = new GroupRepository(Database);
            Workspaces = new WorkspaceRepository(Database);
            History = new MigrationHistoryRepository(Database);
            Export = new ExportService();
            Reader = new LegacyCategoryReader(legacy.ConfigDirectory);

            Backup = new BackupService(Database, Paths, Groups, Workspaces, Export);
            Migrator = new LegacyMigrator(Database, Groups, Reader, History, Backup);
        }

        public LegacyInstall Legacy { get; }

        public string Root => _root;

        public DataPaths Paths { get; }

        public Database Database { get; }

        public GroupRepository Groups { get; }

        public WorkspaceRepository Workspaces { get; }

        public MigrationHistoryRepository History { get; }

        public ExportService Export { get; }

        public LegacyCategoryReader Reader { get; }

        public BackupService Backup { get; }

        public LegacyMigrator Migrator { get; }

        public MigrationReport Run() => Migrator.Migrate(Paths.BackupsDirectory);

        /// <summary>A real .exe file so classification can be checked against the disk.</summary>
        public string WriteExecutable(string folderName, string fileName = "tool.exe")
        {
            string folder = Path.Combine(Legacy.Root, folderName);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A });
            return path;
        }

        public void Dispose()
        {
            try { Database.Dispose(); } catch (Exception) { }
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (Exception) { }
        }
    }

    /// <summary>Reading the frozen 1.x XML shape.</summary>
    public class LegacyReaderTests
    {
        [Fact]
        public void A_group_folder_is_found_by_its_ObjectData_xml()
        {
            using var legacy = new LegacyInstall();
            string real = legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work"));

            var reader = new LegacyCategoryReader(legacy.ConfigDirectory);

            Assert.True(reader.HasLegacyData());
            Assert.Equal(new[] { real }, reader.FindGroupFolders());
        }

        [Fact]
        public void A_folder_without_ObjectData_xml_is_not_a_group()
        {
            // The legacy tree also holds an Icons cache folder and stray files at
            // the top level; treating those as groups would create empty entries.
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work"));
            Directory.CreateDirectory(Path.Combine(legacy.ConfigDirectory, "NotAGroup"));
            File.WriteAllText(Path.Combine(legacy.ConfigDirectory, "stray.txt"), "x");

            var reader = new LegacyCategoryReader(legacy.ConfigDirectory);

            Assert.Single(reader.FindGroupFolders());
        }

        [Fact]
        public void An_empty_or_missing_config_folder_reports_no_data()
        {
            using var legacy = new LegacyInstall();

            var reader = new LegacyCategoryReader(legacy.ConfigDirectory);

            Assert.False(reader.HasLegacyData());
            Assert.Empty(reader.FindGroupFolders());
        }

        [Fact]
        public void Every_legacy_field_is_read()
        {
            using var legacy = new LegacyInstall();
            string folder = legacy.AddGroup("Work",
                LegacyInstall.FullCategoryXml("Work", 7, 42.5, false, "#203040",
                    new LegacyShortcutSpec("Terminal", @"C:\Windows\System32\cmd.exe", "/k dir", @"C:\Users", false)));

            var reader = new LegacyCategoryReader(legacy.ConfigDirectory);
            LegacyCategory? category = reader.Read(folder);

            Assert.NotNull(category);
            Assert.Equal("Work", category!.Name);
            Assert.Equal(7, category.Width);
            Assert.Equal(42.5, category.Opacity);
            Assert.False(category.AllowOpenAll);
            Assert.Equal("#203040", category.ColorString);
            Assert.Single(category.ShortcutList);
            Assert.Equal(@"C:\Windows\System32\cmd.exe", category.ShortcutList[0].FilePath);
            Assert.Equal("Terminal", category.ShortcutList[0].Name);
            Assert.Equal("/k dir", category.ShortcutList[0].Arguments);
            Assert.Equal(@"C:\Users", category.ShortcutList[0].WorkingDirectory);
            Assert.False(category.ShortcutList[0].IsWindowsApp);
        }

        [Fact]
        public void An_unknown_element_from_a_newer_patch_is_ignored()
        {
            using var legacy = new LegacyInstall();
            string xml = LegacyInstall.CategoryXml("Work", new LegacyShortcutSpec("a", @"C:\a.exe", "", "", false))
                .Replace("</Category>", "<SomeFutureFlag>7</SomeFutureFlag></Category>");
            string folder = legacy.AddGroup("Work", xml);

            var reader = new LegacyCategoryReader(legacy.ConfigDirectory);

            Assert.NotNull(reader.Read(folder));
        }

        [Fact]
        public void A_DTD_cannot_be_used_to_exhaust_memory()
        {
            using var legacy = new LegacyInstall();
            string folder = legacy.AddGroup("Evil",
                "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY a \"aaaa\">]><Category><Name>&a;</Name></Category>");

            var reader = new LegacyCategoryReader(legacy.ConfigDirectory);

            // Prohibited rather than ignored, so the file is simply unreadable and
            // the migration records it instead of expanding anything.
            Assert.Null(reader.Read(folder));
        }

        [Fact]
        public void A_group_icon_is_found_when_present()
        {
            using var legacy = new LegacyInstall();
            string folder = legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work"));

            Assert.Null(LegacyCategoryReader.FindGroupIcon(folder));

            legacy.AddGroupIcon("Work", "icon bytes");

            Assert.NotNull(LegacyCategoryReader.FindGroupIcon(folder));
        }
    }

    /// <summary>The migration itself, end to end against a real 1.x layout.</summary>
    public class LegacyMigrationTests
    {
        [Fact]
        public void Groups_and_shortcuts_are_imported()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.FullCategoryXml("Work", 7, 30d, true, "#1f1f1f",
                new LegacyShortcutSpec("Terminal", @"C:\Windows\System32\cmd.exe", "/k dir", @"C:\Users", false),
                new LegacyShortcutSpec("Notepad", @"C:\Windows\System32\notepad.exe", "", "", false)));

            using var harness = new MigrationHarness(legacy);

            Assert.True(harness.Migrator.ShouldRun());
            MigrationReport report = harness.Run();

            Assert.True(report.Succeeded);
            Assert.Equal(1, report.GroupsImportedCount);
            Assert.Equal(2, report.ShortcutsImported);

            Group? group = harness.Groups.GetByName("Work");
            Assert.NotNull(group);
            Assert.Equal(7, group!.Width);
            Assert.Equal(30d, group.Opacity);

            List<ShortcutItem> shortcuts = harness.Groups.GetShortcuts(group.Id);
            Assert.Equal(2, shortcuts.Count);
            Assert.Equal("Terminal", shortcuts[0].Name);
            Assert.Equal("/k dir", shortcuts[0].Arguments);
            Assert.Equal(@"C:\Users", shortcuts[0].WorkingDirectory);
        }

        [Fact]
        public void Shortcut_order_is_preserved()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work",
                new LegacyShortcutSpec("first", @"C:\1.exe", "", "", false),
                new LegacyShortcutSpec("second", @"C:\2.exe", "", "", false),
                new LegacyShortcutSpec("third", @"C:\3.exe", "", "", false)));

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            Group group = harness.Groups.GetByName("Work")!;
            List<string> names = harness.Groups.GetShortcuts(group.Id).Select(s => s.Name).ToList();

            Assert.Equal(new[] { "first", "second", "third" }, names);
        }

        [Fact]
        public void A_folder_name_becomes_a_display_name_with_spaces()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work_Stuff", LegacyInstall.CategoryXml("Work_Stuff"));

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            Group? group = harness.Groups.GetByName("Work Stuff");

            Assert.NotNull(group);
            // The folder form is kept so a link back to the legacy data still works.
            Assert.Equal("Work_Stuff", group!.LegacyName);
        }

        [Fact]
        public void The_legacy_open_all_flag_is_carried_over()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Enabled", LegacyInstall.FullCategoryXml("Enabled", 5, 10d, true, "#1f1f1f"));
            legacy.AddGroup("Disabled", LegacyInstall.FullCategoryXml("Disabled", 5, 10d, false, "#1f1f1f"));

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            Assert.True(harness.Groups.GetByName("Enabled")!.OpenAllEnabled);
            Assert.False(harness.Groups.GetByName("Disabled")!.OpenAllEnabled);
        }

        [Fact]
        public void Migrated_shortcuts_are_enabled()
        {
            // The legacy editor had no per-shortcut enable flag; everything that was
            // in a folder was launchable, so nothing may arrive disabled.
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work",
                new LegacyShortcutSpec("a", @"C:\a.exe", "", "", false)));

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            Group group = harness.Groups.GetByName("Work")!;

            Assert.All(harness.Groups.GetShortcuts(group.Id), s => Assert.True(s.Enabled));
        }

        [Fact]
        public void A_folder_shortcut_is_typed_as_a_folder_not_an_executable()
        {
            // This is the legacy crash: a folder was stored as if it were a program
            // and ExtractAssociatedIcon threw on it at launch time.
            using var legacy = new LegacyInstall();
            string folderPath = Path.Combine(legacy.Root, "Documents");
            Directory.CreateDirectory(folderPath);

            legacy.AddGroup("Files", LegacyInstall.CategoryXml("Files",
                new LegacyShortcutSpec("Documents", folderPath)));

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            Group group = harness.Groups.GetByName("Files")!;
            ShortcutItem item = harness.Groups.GetShortcuts(group.Id).Single();

            Assert.Equal(ShortcutType.Folder, item.Type);
        }

        [Fact]
        public void A_target_that_no_longer_exists_keeps_a_meaningful_type()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work",
                new LegacyShortcutSpec("gone", @"C:\definitely\not\here\app.lnk", "", "", false)));

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            Group group = harness.Groups.GetByName("Work")!;
            ShortcutItem item = harness.Groups.GetShortcuts(group.Id).Single();

            Assert.Equal(ShortcutType.Lnk, item.Type);
            Assert.Equal(@"C:\definitely\not\here\app.lnk", item.Target);
        }

        [Fact]
        public void A_real_executable_is_typed_from_its_extension()
        {
            using var legacy = new LegacyInstall();
            string exe = harness_exe(legacy);

            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work", new LegacyShortcutSpec("tool", exe)));

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            Group group = harness.Groups.GetByName("Work")!;

            Assert.Equal(ShortcutType.Exe, harness.Groups.GetShortcuts(group.Id).Single().Type);
        }

        private static string harness_exe(LegacyInstall legacy)
        {
            string folder = Path.Combine(legacy.Root, "bin");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "tool.exe");
            File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A });
            return path;
        }

        [Fact]
        public void A_windows_store_shortcut_keeps_its_flag_and_its_type()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Store", LegacyInstall.CategoryXml("Store",
                new LegacyShortcutSpec("Photos", @"shell:AppsFolder\Microsoft.Windows.Photos_8wekyb3d8bbwe!App", "", "", true)));

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            Group group = harness.Groups.GetByName("Store")!;
            ShortcutItem item = harness.Groups.GetShortcuts(group.Id).Single();

            Assert.Equal(ShortcutType.Uwp, item.Type);
            Assert.True(item.IsLegacyWindowsApp);
        }

        [Fact]
        public void An_entry_with_no_path_is_skipped_and_reported()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work",
                new LegacyShortcutSpec("", "", "", "", false),
                new LegacyShortcutSpec("good", @"C:\good.exe", "", "", false)));

            using var harness = new MigrationHarness(legacy);
            MigrationReport report = harness.Run();

            Assert.Equal(1, report.ShortcutsImported);
            Assert.Equal(1, report.ShortcutsSkipped);
            Assert.Contains(report.Entries, e => e.Severity == MigrationSeverity.Warning);
        }

        [Fact]
        public void A_group_whose_xml_is_broken_does_not_stop_the_others()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Broken", "<Category><Name>Broken</Name>");   // never closed
            legacy.AddGroup("Fine", LegacyInstall.CategoryXml("Fine", new LegacyShortcutSpec("a", @"C:\a.exe", "", "", false)));

            using var harness = new MigrationHarness(legacy);
            MigrationReport report = harness.Run();

            Assert.NotNull(harness.Groups.GetByName("Fine"));
            Assert.Null(harness.Groups.GetByName("Broken"));
            Assert.False(report.Succeeded);
            Assert.Contains(report.Entries, e => e.Severity == MigrationSeverity.Error && e.GroupName == "Broken");
        }

        [Fact]
        public void The_original_config_folder_is_left_byte_identical()
        {
            // The whole point of migration: a user can still go back to 1.x.
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work", new LegacyShortcutSpec("a", @"C:\a.exe", "", "", false)));
            legacy.AddGroupIcon("Work", "icon bytes");

            var before = Snapshot(legacy.ConfigDirectory);

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            Assert.Equal(before, Snapshot(legacy.ConfigDirectory));
        }

        [Fact]
        public void A_backup_of_the_legacy_data_is_written_before_anything_is_imported()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work", new LegacyShortcutSpec("a", @"C:\a.exe", "", "", false)));

            using var harness = new MigrationHarness(legacy);
            MigrationReport report = harness.Run();

            Assert.NotEmpty(report.BackupArchivePath);
            Assert.True(File.Exists(report.BackupArchivePath));

            using var archive = ZipFile.OpenRead(report.BackupArchivePath);
            string[] entries = archive.Entries.Select(e => e.FullName).ToArray();

            Assert.Contains(entries, e => e.EndsWith("Work/ObjectData.xml", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(entries, e => e.EndsWith("backup.json", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Nothing_is_imported_when_the_backup_cannot_be_written()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work", new LegacyShortcutSpec("a", @"C:\a.exe", "", "", false)));

            using var harness = new MigrationHarness(legacy);

            // A path that cannot be created: a file already sits where the backup
            // folder needs to go.
            File.WriteAllText(Path.Combine(harness.Root, "blocked"), "x");

            MigrationReport report = harness.Migrator.Migrate(Path.Combine(harness.Root, "blocked", "inner"));

            Assert.False(report.Succeeded);
            Assert.Empty(harness.Groups.GetAll());
            Assert.Contains(report.Entries, e => e.Stage == "backup");
        }

        [Fact]
        public void Running_twice_does_not_duplicate_anything()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work", new LegacyShortcutSpec("a", @"C:\a.exe", "", "", false)));

            using var harness = new MigrationHarness(legacy);

            harness.Run();
            Assert.False(harness.Migrator.ShouldRun());
            Assert.False(harness.History.HasCompleted("some-other-source"));

            MigrationReport second = harness.Run();

            Assert.Single(harness.Groups.GetAll());
            Group group = harness.Groups.GetByName("Work")!;
            Assert.Single(harness.Groups.GetShortcuts(group.Id));
            Assert.Equal(0, second.GroupsImportedCount);
        }

        [Fact]
        public void The_run_is_recorded_in_history()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work", new LegacyShortcutSpec("a", @"C:\a.exe", "", "", false)));

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            Assert.True(harness.History.HasCompleted(LegacyMigrator.SourceName));

            var entry = harness.History.GetAll().First();
            Assert.Equal("Completed", entry.Status);
            Assert.Equal(1, entry.Groups);
            Assert.NotEmpty(entry.BackupPath);
        }

        [Fact]
        public void A_report_file_is_written_next_to_the_logs()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work", new LegacyShortcutSpec("a", @"C:\a.exe", "", "", false)));

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            string path = Path.Combine(harness.Paths.LogsDirectory, "migration-report.json");
            Assert.True(File.Exists(path));

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(1, document.RootElement.GetProperty("groupsImportedCount").GetInt32());
            Assert.True(document.RootElement.GetProperty("succeeded").GetBoolean());
        }

        [Fact]
        public void Fields_with_no_equivalent_are_reported_rather_than_dropped_silently()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work"));

            using var harness = new MigrationHarness(legacy);
            MigrationReport report = harness.Run();

            Assert.NotEmpty(report.UnsupportedFields);
            Assert.All(report.UnsupportedFields, f => Assert.False(string.IsNullOrWhiteSpace(f)));
        }

        [Fact]
        public void A_group_icon_is_carried_over_when_present()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work"));
            legacy.AddGroupIcon("Work", "icon bytes");

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            Group group = harness.Groups.GetByName("Work")!;

            Assert.Contains("GroupIcon.ico", group.IconId);
        }

        [Fact]
        public void A_missing_group_icon_is_reported_instead_of_ignored()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work"));

            using var harness = new MigrationHarness(legacy);
            MigrationReport report = harness.Run();

            Group group = harness.Groups.GetByName("Work")!;

            Assert.Equal(string.Empty, group.IconId);
            Assert.Contains(report.UnsupportedFields, f => f.Contains("GroupIcon.ico"));
        }

        [Fact]
        public void An_install_with_no_legacy_data_reports_cleanly_and_changes_nothing()
        {
            using var legacy = new LegacyInstall();
            using var harness = new MigrationHarness(legacy);

            Assert.False(harness.Migrator.ShouldRun());

            MigrationReport report = harness.Run();

            Assert.True(report.Succeeded);
            Assert.Empty(report.GroupsImported);
            Assert.Empty(harness.Groups.GetAll());
            // Nothing was read, so nothing needed backing up.
            Assert.Empty(report.BackupArchivePath);
        }

        [Fact]
        public void Environment_variables_in_a_path_are_expanded()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Work", LegacyInstall.CategoryXml("Work",
                new LegacyShortcutSpec("temp", @"%TEMP%\scratch.exe", "", "", false)));

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            Group group = harness.Groups.GetByName("Work")!;
            ShortcutItem item = harness.Groups.GetShortcuts(group.Id).Single();

            Assert.DoesNotContain("%TEMP%", item.Target);
            // The raw form is kept so the migration stays auditable.
            Assert.Contains("%TEMP%", item.LegacyFilePath);
        }

        [Fact]
        public void Migrated_groups_appear_in_the_order_the_folders_are_listed()
        {
            using var legacy = new LegacyInstall();
            legacy.AddGroup("Zulu", LegacyInstall.CategoryXml("Zulu"));
            legacy.AddGroup("Alpha", LegacyInstall.CategoryXml("Alpha"));
            legacy.AddGroup("Mike", LegacyInstall.CategoryXml("Mike"));

            using var harness = new MigrationHarness(legacy);
            harness.Run();

            List<string> names = harness.Groups.GetAll().Select(g => g.Name).ToList();

            // The reader sorts folders, so the dashboard order is deterministic
            // rather than dependent on file system enumeration order.
            Assert.Equal(new[] { "Alpha", "Mike", "Zulu" }, names);
        }

        private static Dictionary<string, string> Snapshot(string root)
        {
            var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                snapshot[file.Substring(root.Length)] = File.ReadAllText(file);

            return snapshot;
        }
    }

    /// <summary>The backup service, which migration depends on.</summary>
    public class BackupServiceTests
    {
        private static MigrationHarness Build(Action<MigrationHarness>? seed)
        {
            var harness = new MigrationHarness(new LegacyInstall());
            seed?.Invoke(harness);
            return harness;
        }

        private static MigrationHarness BuildWithGroup(string name = "Work")
        {
            return Build(h => SeedGroup(h, name));
        }

        private static void SeedGroup(MigrationHarness harness, string name)
        {
            Group group = new Group { Name = name, Width = 6 };
            harness.Groups.Upsert(group);
            harness.Groups.ReplaceShortcuts(group.Id, new[]
            {
                new ShortcutItem
                {
                    GroupId = group.Id,
                    Name = "a",
                    Type = ShortcutType.Exe,
                    Target = @"C:\a.exe",
                    Arguments = "--flag",
                    WorkingDirectory = @"C:\work"
                }
            });
        }

        [Fact]
        public void A_backup_contains_a_database_an_export_and_a_manifest()
        {
            using MigrationHarness harness = BuildWithGroup("Work");

            string path = harness.Backup.CreateBackup("unit-test");

            Assert.True(File.Exists(path));

            using var archive = ZipFile.OpenRead(path);
            string[] entries = archive.Entries.Select(e => e.FullName).ToArray();

            Assert.Contains(BackupService.ManifestEntryName, entries);
            Assert.Contains(BackupService.DatabaseEntryName, entries);
            Assert.Contains(BackupService.ExportEntryName, entries);
        }

        [Fact]
        public void A_valid_backup_passes_validation()
        {
            using MigrationHarness harness = BuildWithGroup("Work");

            string path = harness.Backup.CreateBackup();
            BackupValidationResult result = harness.Backup.Validate(path);

            Assert.True(result.IsValid, string.Join("; ", result.Problems));
            Assert.True(result.HasDatabase);
            Assert.True(result.HasJsonExport);
        }

        [Fact]
        public void A_missing_file_fails_validation_with_a_reason()
        {
            using MigrationHarness harness = BuildWithGroup("Work");

            BackupValidationResult result = harness.Backup.Validate(Path.Combine(harness.Root, "nope.zip"));

            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Problems);
        }

        [Fact]
        public void A_file_that_is_not_a_zip_fails_validation_without_throwing()
        {
            using MigrationHarness harness = Build(null);
            string path = Path.Combine(harness.Root, "not-a-zip.zip");
            File.WriteAllText(path, "this is not a zip archive");

            BackupValidationResult result = harness.Backup.Validate(path);

            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Problems);
        }

        [Fact]
        public void An_empty_archive_fails_validation()
        {
            using MigrationHarness harness = Build(null);
            string path = Path.Combine(harness.Root, "empty.zip");

            using (var stream = new FileStream(path, FileMode.Create))
            using (new ZipArchive(stream, ZipArchiveMode.Create))
            {
            }

            BackupValidationResult result = harness.Backup.Validate(path);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void Restoring_brings_back_deleted_content()
        {
            using MigrationHarness harness = BuildWithGroup("Work");

            string path = harness.Backup.CreateBackup();
            Group survivor = harness.Groups.GetByName("Work")!;
            harness.Groups.Delete(survivor.Id);

            Assert.Null(harness.Groups.GetByName("Work"));

            RestoreResult result = harness.Backup.Restore(path);

            Assert.True(result.Success, string.Join("; ", result.Problems));
            Assert.NotNull(harness.Groups.GetByName("Work"));
        }

        [Fact]
        public void Restoring_copies_the_live_database_first()
        {
            using MigrationHarness harness = BuildWithGroup("Work");
            string path = harness.Backup.CreateBackup();

            RestoreResult result = harness.Backup.Restore(path);

            Assert.True(result.Success, string.Join("; ", result.Problems));
            Assert.True(File.Exists(result.PreviousDatabaseBackup));
        }

        [Fact]
        public void Restoring_the_same_backup_twice_does_not_fail_on_a_duplicate_key()
        {
            using MigrationHarness harness = BuildWithGroup("Work");
            string path = harness.Backup.CreateBackup();

            Assert.True(harness.Backup.Restore(path).Success);
            RestoreResult second = harness.Backup.Restore(path);

            Assert.True(second.Success, string.Join("; ", second.Problems));
            Assert.Single(harness.Groups.GetAll());
        }

        [Fact]
        public void Pruning_keeps_the_requested_number_of_backups()
        {
            using MigrationHarness harness = BuildWithGroup("Work");

            // Timestamped to the second, so distinct names need distinct reasons.
            harness.Backup.CreateBackup("one");
            harness.Backup.CreateBackup("two");
            harness.Backup.CreateBackup("three");

            int removed = harness.Backup.PruneOldBackups(2);

            Assert.Equal(1, removed);
            Assert.Equal(2, Directory.GetFiles(harness.Paths.BackupsDirectory, "TaskbarGroups-backup-*.zip").Length);
        }

        [Fact]
        public void Pruning_never_returns_below_one()
        {
            using MigrationHarness harness = BuildWithGroup("Work");
            harness.Backup.CreateBackup("one");

            harness.Backup.PruneOldBackups(0);

            Assert.Single(Directory.GetFiles(harness.Paths.BackupsDirectory, "TaskbarGroups-backup-*.zip"));
        }
    }

    /// <summary>The versioned JSON document that export, import and restore share.</summary>
    public class ExportImportTests
    {
        private static MigrationHarness BuildWithData()
        {
            var harness = new MigrationHarness(new LegacyInstall());

            Group group = new Group { Name = "Work", Width = 9, Theme = GroupTheme.Mica, Opacity = 33d };
            harness.Groups.Upsert(group);
            harness.Groups.ReplaceShortcuts(group.Id, new[]
            {
                new ShortcutItem
                {
                    GroupId = group.Id,
                    Name = "Terminal",
                    Type = ShortcutType.Cmd,
                    Target = @"C:\Windows\System32\cmd.exe",
                    Arguments = "/k dir",
                    WorkingDirectory = @"C:\Users",
                    RunAsAdministrator = true,
                    WindowState = WindowStatePreference.Maximized,
                    SortOrder = 0
                }
            });

            Workspace workspace = new Workspace { Name = "Dev", LaunchDelayMs = 900 };
            workspace.Items.Add(new WorkspaceItem
            {
                WorkspaceId = workspace.Id,
                Executable = @"C:\Windows\System32\cmd.exe",
                ProcessMatch = "cmd",
                NormalizedPlacement = PlacementAnchor.LeftHalf,
                SortOrder = 0
            });
            harness.Workspaces.Upsert(workspace);

            return harness;
        }

        [Fact]
        public void An_export_carries_every_field_of_a_group()
        {
            using MigrationHarness harness = BuildWithData();

            string json = harness.Export.Export(harness.Backup.BuildExportDocument());

            ImportResult imported = harness.Export.Import(json);

            Assert.Empty(imported.Problems);
            ExportGroup exported = imported.Document.Groups.Single();
            Assert.Equal("Work", exported.Name);
            Assert.Equal(9, exported.Width);
            Assert.Equal(GroupTheme.Mica, exported.Theme);
            Assert.Equal(33d, exported.Opacity);
        }

        [Fact]
        public void A_shortcut_survives_the_round_trip()
        {
            using MigrationHarness harness = BuildWithData();

            ImportResult imported = harness.Export.Import(harness.Export.Export(harness.Backup.BuildExportDocument()));
            ExportShortcut exported = imported.Document.Groups.Single().Shortcuts.Single();

            Assert.Equal("Terminal", exported.Name);
            Assert.Equal(ShortcutType.Cmd, exported.Type);
            Assert.Equal("/k dir", exported.Arguments);
            Assert.Equal(@"C:\Users", exported.WorkingDirectory);
            Assert.True(exported.RunAsAdministrator);
            Assert.Equal(WindowStatePreference.Maximized, exported.WindowState);
        }

        [Fact]
        public void A_workspace_survives_the_round_trip()
        {
            using MigrationHarness harness = BuildWithData();

            ImportResult imported = harness.Export.Import(harness.Export.Export(harness.Backup.BuildExportDocument()));
            ExportWorkspace workspace = imported.Document.Workspaces.Single();

            Assert.Equal("Dev", workspace.Name);
            Assert.Equal(900, workspace.LaunchDelayMs);
            Assert.Equal(PlacementAnchor.LeftHalf, workspace.Items.Single().NormalizedPlacement);
        }

        [Fact]
        public void Importing_nothing_reports_the_problem_instead_of_throwing()
        {
            using MigrationHarness harness = BuildWithData();

            ImportResult result = harness.Export.Import("   ");

            Assert.NotEmpty(result.Problems);
        }

        [Fact]
        public void Malformed_json_is_reported_and_yields_an_empty_document()
        {
            using MigrationHarness harness = BuildWithData();

            ImportResult result = harness.Export.Import("{ this is not json ");

            Assert.NotEmpty(result.Problems);
            Assert.NotNull(result.Document);
            Assert.Empty(result.Document.Groups);
        }

        [Fact]
        public void A_document_from_a_newer_schema_version_is_refused_with_a_reason()
        {
            using MigrationHarness harness = BuildWithData();

            string json = harness.Export.Export(harness.Backup.BuildExportDocument())
                .Replace("\"schemaVersion\": 2", "\"schemaVersion\": 99");

            ImportResult result = harness.Export.Import(json);

            Assert.NotEmpty(result.Problems);
            Assert.Contains(result.Problems, p => p.Contains("99"));
        }

        [Fact]
        public void A_group_without_a_name_is_reported_and_the_rest_still_loads()
        {
            using MigrationHarness harness = BuildWithData();

            string json = harness.Export.Export(harness.Backup.BuildExportDocument())
                .Replace("\"name\": \"Work\"", "\"name\": \"\"", StringComparison.Ordinal);

            ImportResult result = harness.Export.Import(json);

            Assert.NotEmpty(result.Problems);
            // The workspace still came across.
            Assert.Single(result.Document.Workspaces);
        }

        [Fact]
        public void An_export_holds_no_absolute_path_from_the_build_machine()
        {
            using MigrationHarness harness = BuildWithData();

            string json = harness.Export.Export(harness.Backup.BuildExportDocument());

            // Only the user's own targets appear; no developer or install paths.
            Assert.DoesNotContain("Users\\PC", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("taskbar-groups\\src", json, StringComparison.OrdinalIgnoreCase);
        }
    }
}