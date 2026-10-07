using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Data.Json;
using TaskbarGroups.Data.Repositories;
using TaskbarGroups.Data.Storage;
using Xunit;

namespace TaskbarGroups.DataTests
{
    /// <summary>
    /// A throwaway data directory per test, deleted afterwards.
    /// </summary>
    /// <remarks>
    /// Each test gets its own SQLite file rather than sharing one. A shared
    /// database would make tests order-dependent, and a real backup could then
    /// restore another test's data.
    /// </remarks>
    internal sealed class TestDataDirectory : IDisposable
    {
        public TestDataDirectory(string name)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "taskbargroups-data-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "-" + name);
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        /// <summary>
        /// Builds a path several folders deep. The database creates missing
        /// directories, so pointing it at "a/b/c.db" must work.
        /// </summary>
        public string File(params string[] segments)
        {
            string result = Path;
            foreach (string segment in segments) result = System.IO.Path.Combine(result, segment);
            return result;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>Schema creation and integrity.</summary>
    public class SchemaTests : IDisposable
    {
        private readonly TestDataDirectory _directory = new TestDataDirectory("schema");

        public void Dispose() => _directory.Dispose();

        [Fact]
        public void Initialising_creates_a_usable_database()
        {
            using var database = new Database(_directory.File("TaskbarGroups.db"));

            int version = SchemaMigrator.Initialize(database);

            Assert.Equal(SchemaMigrator.TargetVersion, version);
            Assert.True(database.IsHealthy(out string error), error);
        }

        [Fact]
        public void Initialising_twice_is_harmless()
        {
            using var database = new Database(_directory.File("TaskbarGroups.db"));

            int first = SchemaMigrator.Initialize(database);
            int second = SchemaMigrator.Initialize(database);

            Assert.Equal(first, second);
            Assert.True(database.IsHealthy(out _));
        }

        [Fact]
        public void A_fresh_database_passes_its_integrity_check()
        {
            using var database = new Database(_directory.File("TaskbarGroups.db"));
            SchemaMigrator.Initialize(database);

            Assert.True(database.CheckIntegrity(out string error), error);
        }

        [Fact]
        public void Every_expected_table_exists()
        {
            using var database = new Database(_directory.File("TaskbarGroups.db"));
            SchemaMigrator.Initialize(database);

            string[] expected =
            {
                "Groups", "ShortcutItems", "Workspaces", "WorkspaceItems",
                "WindowPlacements", "Icons", "Settings", "Themes", "MigrationHistory"
            };

            foreach (string table in expected)
            {
                using SqliteProbe probe = SqliteProbe.Query(
                    database, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;",
                    ("$name", table));

                Assert.True(probe.Count > 0, "missing table: " + table);
            }
        }

        [Fact]
        public void Foreign_keys_are_enforced()
        {
            using var database = new Database(_directory.File("TaskbarGroups.db"));
            SchemaMigrator.Initialize(database);

            // A shortcut with no group must be rejected: that is what stops
            // orphaned rows accumulating when a group is renamed.
            Assert.ThrowsAny<Exception>(() =>
            {
                using SqliteProbe probe = SqliteProbe.Execute(
                    database,
                    "INSERT INTO ShortcutItems (Id, GroupId, Name, Type, Target) " +
                    "VALUES ('x', 'no-such-group', 'n', 'Exe', 't');");
            });
        }

        [Fact]
        public void Deleting_a_group_removes_its_shortcuts()
        {
            using var database = new Database(_directory.File("TaskbarGroups.db"));
            SchemaMigrator.Initialize(database);

            var groups = new GroupRepository(database);
            Group group = NewGroup("Doomed");

            groups.Upsert(group);
            groups.ReplaceShortcuts(group.Id, new[] { SchemaTests.NewShortcut(group.Id, "a.exe") });

            Assert.Single(groups.GetShortcuts(group.Id));

            groups.Delete(group.Id);

            Assert.Empty(groups.GetShortcuts(group.Id));
        }

        [Fact]
        public void Two_groups_cannot_share_a_name()
        {
            using var database = new Database(_directory.File("TaskbarGroups.db"));
            SchemaMigrator.Initialize(database);

            var groups = new GroupRepository(database);
            groups.Upsert(NewGroup("Unique"));

            Assert.ThrowsAny<Exception>(() => groups.Upsert(NewGroup("unique")));
        }

        [Fact]
        public void A_database_can_be_constructed_when_no_file_exists_yet()
        {
            // First run: the folder may not even exist. The constructor creates the
            // folder, and the connection opens lazily so nothing is created until a
            // caller actually needs the database.
            string path = _directory.File("nested", "deeper", "TaskbarGroups.db");

            using var database = new Database(path);

            Assert.True(Directory.Exists(System.IO.Path.GetDirectoryName(path)));
            Assert.False(File.Exists(path));

            SchemaMigrator.Initialize(database);

            Assert.True(File.Exists(path));
        }

        internal static Group NewGroup(string name, Action<Group>? configure = null)
        {
            var group = new Group { Name = name };
            configure?.Invoke(group);
            return group;
        }

        internal static ShortcutItem NewShortcut(Guid groupId, string target, int order = 0)
        {
            return new ShortcutItem
            {
                GroupId = groupId,
                Name = Path.GetFileNameWithoutExtension(target),
                Type = ShortcutType.Exe,
                Target = target,
                SortOrder = order
            };
        }
    }

    /// <summary>Round-tripping groups and their shortcuts.</summary>
    public class GroupRepositoryTests : IDisposable
    {
        private readonly TestDataDirectory _directory = new TestDataDirectory("groups");
        private readonly Database _database;
        private readonly GroupRepository _groups;

        public GroupRepositoryTests()
        {
            _database = new Database(_directory.File("TaskbarGroups.db"));
            SchemaMigrator.Initialize(_database);
            _groups = new GroupRepository(_database);
        }

        public void Dispose()
        {
            _database.Dispose();
            _directory.Dispose();
        }

        [Fact]
        public void A_saved_group_reads_back_identically()
        {
            var group = SchemaTests.NewGroup("Round trip", g =>
            {
                g.Description = "Every field";
                g.Width = 7;
                g.Theme = GroupTheme.Mica;
                g.Opacity = 42.5;
                g.CornerRadius = 12;
                g.ShadowEnabled = false;
                g.BlurEnabled = false;
                g.AnimationEnabled = false;
                g.OpenAllEnabled = false;
                g.SortMode = GroupSortMode.NameDescending;
                g.MonitorPreference = MonitorPreference.Primary;
                g.PreferredMonitorDeviceName = @"\\.\DISPLAY2";
                g.IconId = @"C:\icons\group.png";
                g.LegacyName = "Round_trip";
            });

            _groups.Upsert(group);

            Group? loaded = _groups.GetById(group.Id);

            Assert.NotNull(loaded);
            Assert.Equal(group.Id, loaded!.Id);
            Assert.Equal("Round trip", loaded.Name);
            Assert.Equal("Every field", loaded.Description);
            Assert.Equal(7, loaded.Width);
            Assert.Equal(GroupTheme.Mica, loaded.Theme);
            Assert.Equal(42.5, loaded.Opacity);
            Assert.Equal(12, loaded.CornerRadius);
            Assert.False(loaded.ShadowEnabled);
            Assert.False(loaded.BlurEnabled);
            Assert.False(loaded.AnimationEnabled);
            Assert.False(loaded.OpenAllEnabled);
            Assert.Equal(GroupSortMode.NameDescending, loaded.SortMode);
            Assert.Equal(MonitorPreference.Primary, loaded.MonitorPreference);
            Assert.Equal(@"\\.\DISPLAY2", loaded.PreferredMonitorDeviceName);
            Assert.Equal("Round_trip", loaded.LegacyName);
        }

        [Fact]
        public void Saving_twice_updates_rather_than_duplicates()
        {
            Group group = SchemaTests.NewGroup("Twice");
            _groups.Upsert(group);

            group.Width = 9;
            _groups.Upsert(group);

            Assert.Single(_groups.GetAll());
            Assert.Equal(9, _groups.GetById(group.Id)!.Width);
        }

        [Fact]
        public void A_group_is_found_by_name_case_insensitively()
        {
            _groups.Upsert(SchemaTests.NewGroup("Work"));

            Assert.NotNull(_groups.GetByName("work"));
            Assert.NotNull(_groups.GetByName("WORK"));
            Assert.Null(_groups.GetByName("Play"));
        }

        [Fact]
        public void An_empty_name_is_rejected()
        {
            Assert.Throws<ArgumentException>(() => _groups.Upsert(new Group { Name = "   " }));
        }

        [Fact]
        public void A_null_group_is_rejected()
        {
            Assert.Throws<ArgumentNullException>(() => _groups.Upsert(null!));
        }

        [Fact]
        public void Shortcuts_round_trip_in_order()
        {
            Group group = SchemaTests.NewGroup("Ordered");
            _groups.Upsert(group);

            var items = new List<ShortcutItem>
            {
                SchemaTests.NewShortcut(group.Id, @"C:\apps\first.exe", 0),
                SchemaTests.NewShortcut(group.Id, @"C:\apps\second.exe", 1),
                SchemaTests.NewShortcut(group.Id, @"C:\apps\third.exe", 2)
            };

            _groups.ReplaceShortcuts(group.Id, items);

            List<ShortcutItem> loaded = _groups.GetShortcuts(group.Id);

            Assert.Equal(3, loaded.Count);
            Assert.Equal(new[] { "first", "second", "third" }, loaded.Select(i => i.Name));
        }

        [Fact]
        public void Every_shortcut_field_round_trips()
        {
            Group group = SchemaTests.NewGroup("Fields");
            _groups.Upsert(group);

            var item = new ShortcutItem
            {
                GroupId = group.Id,
                Name = "Terminal",
                Type = ShortcutType.Cmd,
                Target = @"C:\Windows\System32\cmd.exe",
                Arguments = "/k echo hello",
                WorkingDirectory = @"C:\Users",
                IconSource = @"C:\icons\term.png",
                RunAsAdministrator = true,
                WindowState = WindowStatePreference.Maximized,
                PreferredMonitor = MonitorPreference.Specific,
                PreferredMonitorDeviceName = @"\\.\DISPLAY3",
                Enabled = false,
                SortOrder = 4,
                IsAutoDetected = true
            };

            _groups.ReplaceShortcuts(group.Id, new[] { item });

            ShortcutItem loaded = _groups.GetShortcuts(group.Id).Single();

            Assert.Equal(item.Id, loaded.Id);
            Assert.Equal(group.Id, loaded.GroupId);
            Assert.Equal("Terminal", loaded.Name);
            Assert.Equal(ShortcutType.Cmd, loaded.Type);
            Assert.Equal(@"C:\Windows\System32\cmd.exe", loaded.Target);
            Assert.Equal("/k echo hello", loaded.Arguments);
            Assert.Equal(@"C:\Users", loaded.WorkingDirectory);
            Assert.Equal(@"C:\icons\term.png", loaded.IconSource);
            Assert.True(loaded.RunAsAdministrator);
            Assert.Equal(WindowStatePreference.Maximized, loaded.WindowState);
            Assert.Equal(MonitorPreference.Specific, loaded.PreferredMonitor);
            Assert.Equal(@"\\.\DISPLAY3", loaded.PreferredMonitorDeviceName);
            Assert.False(loaded.Enabled);
            Assert.Equal(4, loaded.SortOrder);
            Assert.True(loaded.IsAutoDetected);
        }

        [Fact]
        public void Replacing_shortcuts_removes_the_previous_set()
        {
            Group group = SchemaTests.NewGroup("Replace");
            _groups.Upsert(group);

            _groups.ReplaceShortcuts(group.Id, new[]
            {
                SchemaTests.NewShortcut(group.Id, @"C:\a.exe", 0),
                SchemaTests.NewShortcut(group.Id, @"C:\b.exe", 1)
            });

            _groups.ReplaceShortcuts(group.Id, new[] { SchemaTests.NewShortcut(group.Id, @"C:\c.exe", 0) });

            List<ShortcutItem> loaded = _groups.GetShortcuts(group.Id);

            Assert.Single(loaded);
            Assert.Equal("c", loaded[0].Name);
        }

        [Fact]
        public void Replacing_with_nothing_empties_the_group()
        {
            Group group = SchemaTests.NewGroup("Empty");
            _groups.Upsert(group);

            _groups.ReplaceShortcuts(group.Id, new[] { SchemaTests.NewShortcut(group.Id, @"C:\a.exe") });
            _groups.ReplaceShortcuts(group.Id, Array.Empty<ShortcutItem>());

            Assert.Empty(_groups.GetShortcuts(group.Id));
        }

        [Fact]
        public void A_unicode_target_and_name_survive_the_round_trip()
        {
            Group group = SchemaTests.NewGroup("İş Uygulamaları");
            _groups.Upsert(group);

            _groups.ReplaceShortcuts(group.Id, new[]
            {
                new ShortcutItem
                {
                    GroupId = group.Id,
                    Name = "Çalışma",
                    Type = ShortcutType.Exe,
                    Target = @"C:\Uygulamalar\İş.exe"
                }
            });

            Assert.Equal("İş Uygulamaları", _groups.GetById(group.Id)!.Name);
            Assert.Equal("Çalışma", _groups.GetShortcuts(group.Id).Single().Name);
        }

        [Fact]
        public void A_very_long_target_is_not_truncated()
        {
            Group group = SchemaTests.NewGroup("Long");
            _groups.Upsert(group);

            string target = @"C:\" + string.Join(@"\", Enumerable.Repeat("a-fairly-long-folder-name", 12)) + @"\app.exe";
            _groups.ReplaceShortcuts(group.Id, new[] { SchemaTests.NewShortcut(group.Id, target) });

            Assert.Equal(target, _groups.GetShortcuts(group.Id).Single().Target);
        }

        [Fact]
        public void An_unknown_enum_value_in_the_database_does_not_throw()
        {
            // A database written by a newer build could hold a value this build does
            // not know. It has to degrade to the default rather than make the group
            // unreadable.
            Group group = SchemaTests.NewGroup("Future");
            _groups.Upsert(group);

            using (SqliteProbe.Execute(_database,
                       "UPDATE Groups SET Theme = 'Holographic' WHERE Id = $id;", ("$id", group.Id.ToString())))
            {
            }

            Group? loaded = _groups.GetById(group.Id);

            Assert.NotNull(loaded);
            Assert.Equal(GroupTheme.System, loaded!.Theme);
        }

        [Fact]
        public void Groups_come_back_in_the_order_they_were_created()
        {
            // Creation order, not alphabetical: a migrated user's group arrangement
            // came from the 1.x ObjectData.xml sequence and must not be reshuffled.
            _groups.Upsert(SchemaTests.NewGroup("Zebra"));
            _groups.Upsert(SchemaTests.NewGroup("Apple"));
            _groups.Upsert(SchemaTests.NewGroup("Mango"));

            List<string> names = _groups.GetAll().Select(g => g.Name).ToList();

            Assert.Equal(new[] { "Zebra", "Apple", "Mango" }, names);
        }

        [Fact]
        public void The_order_is_stable_across_repeated_reads()
        {
            _groups.Upsert(SchemaTests.NewGroup("Zebra"));
            _groups.Upsert(SchemaTests.NewGroup("Apple"));

            List<string> first = _groups.GetAll().Select(g => g.Name).ToList();
            List<string> second = _groups.GetAll().Select(g => g.Name).ToList();

            Assert.Equal(first, second);
        }

        [Fact]
        public void Saving_an_existing_group_does_not_move_it()
        {
            Group zebra = SchemaTests.NewGroup("Zebra");
            Group apple = SchemaTests.NewGroup("Apple");
            _groups.Upsert(zebra);
            _groups.Upsert(apple);

            zebra.Width = 8;
            _groups.Upsert(zebra);

            List<string> names = _groups.GetAll().Select(g => g.Name).ToList();

            Assert.Equal(new[] { "Zebra", "Apple" }, names);
        }

        [Fact]
        public void A_deleted_group_does_not_let_a_new_one_take_its_place()
        {
            // If a new group reused the deleted group's position, the dashboard would
            // reorder itself behind the user's back.
            Group first = SchemaTests.NewGroup("First");
            Group second = SchemaTests.NewGroup("Second");
            _groups.Upsert(first);
            _groups.Upsert(second);

            _groups.Delete(second.Id);
            _groups.Upsert(SchemaTests.NewGroup("Third"));

            List<string> names = _groups.GetAll().Select(g => g.Name).ToList();

            Assert.Equal(new[] { "First", "Third" }, names);
        }
    }

    /// <summary>Workspaces, their items and saved layouts.</summary>
    public class WorkspaceRepositoryTests : IDisposable
    {
        private readonly TestDataDirectory _directory = new TestDataDirectory("workspaces");
        private readonly Database _database;
        private readonly WorkspaceRepository _workspaces;

        public WorkspaceRepositoryTests()
        {
            _database = new Database(_directory.File("TaskbarGroups.db"));
            SchemaMigrator.Initialize(_database);
            _workspaces = new WorkspaceRepository(_database);
        }

        public void Dispose()
        {
            _database.Dispose();
            _directory.Dispose();
        }

        [Fact]
        public void A_workspace_round_trips_with_its_items()
        {
            var workspace = new Workspace
            {
                Name = "Development",
                Description = "Two monitors",
                LaunchDelayMs = 1500
            };

            workspace.Items.Add(new WorkspaceItem
            {
                Executable = @"C:\Program Files\Code\code.exe",
                ProcessMatch = "code.exe",
                MonitorDeviceName = @"\\.\DISPLAY1",
                NormalizedPlacement = PlacementAnchor.LeftHalf,
                X = 0,
                Y = 0,
                Width = 960,
                Height = 1040,
                WindowState = WindowStatePreference.Normal,
                SortOrder = 0
            });

            workspace.Items.Add(new WorkspaceItem
            {
                Executable = @"C:\Windows\System32\notepad.exe",
                ProcessMatch = "notepad.exe",
                NormalizedPlacement = PlacementAnchor.RightHalf,
                SortOrder = 1
            });

            _workspaces.Upsert(workspace);

            Workspace? loaded = _workspaces.GetById(workspace.Id);

            Assert.NotNull(loaded);
            Assert.Equal("Development", loaded!.Name);
            Assert.Equal(1500, loaded.LaunchDelayMs);
            Assert.Equal(2, loaded.Items.Count);

            WorkspaceItem first = loaded.Items[0];
            Assert.Equal(@"C:\Program Files\Code\code.exe", first.Executable);
            Assert.Equal(@"\\.\DISPLAY1", first.MonitorDeviceName);
            Assert.Equal(PlacementAnchor.LeftHalf, first.NormalizedPlacement);
            Assert.Equal(960, first.Width);
        }

        [Fact]
        public void Items_keep_their_order()
        {
            var workspace = new Workspace { Name = "Ordered" };

            for (int index = 0; index < 5; index++)
            {
                workspace.Items.Add(new WorkspaceItem
                {
                    Executable = @"C:\apps\" + index + ".exe",
                    SortOrder = index
                });
            }

            _workspaces.Upsert(workspace);

            List<string> names = _workspaces.GetById(workspace.Id)!.Items
                .Select(i => Path.GetFileNameWithoutExtension(i.Executable))
                .ToList();

            Assert.Equal(new[] { "0", "1", "2", "3", "4" }, names);
        }

        [Fact]
        public void Saving_a_workspace_twice_does_not_duplicate_its_items()
        {
            var workspace = new Workspace { Name = "Twice" };
            workspace.Items.Add(new WorkspaceItem { Executable = @"C:\a.exe" });

            _workspaces.Upsert(workspace);
            _workspaces.Upsert(workspace);

            Workspace? loaded = _workspaces.GetById(workspace.Id);

            Assert.NotNull(loaded);
            Assert.Single(loaded!.Items);
        }

        [Fact]
        public void A_workspace_is_found_by_name()
        {
            _workspaces.Upsert(new Workspace { Name = "Gaming" });

            Assert.NotNull(_workspaces.GetByName("gaming"));
            Assert.Null(_workspaces.GetByName("Media"));
        }

        [Fact]
        public void An_empty_name_is_rejected()
        {
            Assert.Throws<ArgumentException>(() => _workspaces.Upsert(new Workspace { Name = "" }));
        }

        [Fact]
        public void Placements_round_trip()
        {
            var workspace = new Workspace { Name = "Layout" };
            _workspaces.Upsert(workspace);

            _workspaces.ReplacePlacements(workspace.Id, new[]
            {
                new WindowPlacement
                {
                    WorkspaceId = workspace.Id,
                    Executable = "code.exe",
                    ProcessMatch = "Visual Studio Code",
                    MonitorId = @"\\.\DISPLAY1",
                    X = 100, Y = 200, Width = 1200, Height = 800,
                    WindowState = WindowStatePreference.Maximized
                }
            });

            WindowPlacement loaded = _workspaces.GetPlacements(workspace.Id).Single();

            Assert.Equal("code.exe", loaded.Executable);
            Assert.Equal("Visual Studio Code", loaded.ProcessMatch);
            Assert.Equal(100, loaded.X);
            Assert.Equal(800, loaded.Height);
            Assert.Equal(WindowStatePreference.Maximized, loaded.WindowState);
        }

        [Fact]
        public void Replacing_placements_replaces_the_whole_set()
        {
            var workspace = new Workspace { Name = "Layout2" };
            _workspaces.Upsert(workspace);

            _workspaces.ReplacePlacements(workspace.Id, new[]
            {
                new WindowPlacement { WorkspaceId = workspace.Id, Executable = "a.exe" },
                new WindowPlacement { WorkspaceId = workspace.Id, Executable = "b.exe" }
            });

            _workspaces.ReplacePlacements(workspace.Id, new[]
            {
                new WindowPlacement { WorkspaceId = workspace.Id, Executable = "c.exe" }
            });

            List<WindowPlacement> loaded = _workspaces.GetPlacements(workspace.Id);

            Assert.Single(loaded);
            Assert.Equal("c.exe", loaded[0].Executable);
        }

        [Fact]
        public void Deleting_a_workspace_removes_its_items_and_placements()
        {
            var workspace = new Workspace { Name = "Doomed" };
            workspace.Items.Add(new WorkspaceItem { Executable = @"C:\a.exe" });
            _workspaces.Upsert(workspace);

            _workspaces.ReplacePlacements(workspace.Id, new[]
            {
                new WindowPlacement { WorkspaceId = workspace.Id, Executable = "a.exe" }
            });

            _workspaces.Delete(workspace.Id);

            Assert.Empty(_workspaces.GetPlacements(workspace.Id));
            Assert.Null(_workspaces.GetById(workspace.Id));
        }

        [Fact]
        public void All_workspaces_come_back_with_their_items()
        {
            foreach (string name in new[] { "One", "Two", "Three" })
            {
                var workspace = new Workspace { Name = name };
                workspace.Items.Add(new WorkspaceItem { Executable = name + ".exe" });
                _workspaces.Upsert(workspace);
            }

            List<Workspace> all = _workspaces.GetAll();

            Assert.Equal(3, all.Count);
            Assert.All(all, workspace => Assert.Single(workspace.Items));
        }
    }

    /// <summary>Settings persistence.</summary>
    public class SettingsRepositoryTests : IDisposable
    {
        private readonly TestDataDirectory _directory = new TestDataDirectory("settings");
        private readonly Database _database;
        private readonly SettingsRepository _settings;

        public SettingsRepositoryTests()
        {
            _database = new Database(_directory.File("TaskbarGroups.db"));
            SchemaMigrator.Initialize(_database);
            _settings = new SettingsRepository(_database);
        }

        public void Dispose()
        {
            _database.Dispose();
            _directory.Dispose();
        }

        [Fact]
        public void A_missing_key_returns_the_fallback()
        {
            AppSettings settings = _settings.Load("nothing-here", () => new AppSettings { GroupWidth = 4 });

            Assert.Equal(4, settings.GroupWidth);
        }

        [Fact]
        public void Settings_round_trip_including_enums()
        {
            var settings = new AppSettings
            {
                GroupWidth = 9,
                GroupTheme = GroupTheme.Acrylic,
                StartWithWindows = true,
                ShowGroupHotkey = "Ctrl+Shift+K",
                LoggingEnabled = false
            };

            _settings.Save("test", settings);
            AppSettings loaded = _settings.Load("test", () => new AppSettings());

            Assert.Equal(9, loaded.GroupWidth);
            Assert.Equal(GroupTheme.Acrylic, loaded.GroupTheme);
            Assert.True(loaded.StartWithWindows);
            Assert.Equal("Ctrl+Shift+K", loaded.ShowGroupHotkey);
            Assert.False(loaded.LoggingEnabled);
        }

        [Fact]
        public void Saving_twice_updates_in_place()
        {
            _settings.Save("test", new AppSettings { GroupWidth = 3 });
            _settings.Save("test", new AppSettings { GroupWidth = 6 });

            Assert.Equal(6, _settings.Load("test", () => new AppSettings()).GroupWidth);
        }

        [Fact]
        public void Corrupt_json_falls_back_to_defaults_rather_than_throwing()
        {
            _settings.Save("test", new AppSettings());

            using (SqliteProbe.Execute(_database, "UPDATE Settings SET Value = 'not json' WHERE Key = $key;", ("$key", "test")))
            {
            }

            AppSettings loaded = _settings.Load("test", () => new AppSettings { GroupWidth = 5 });

            Assert.Equal(5, loaded.GroupWidth);
        }

        [Fact]
        public void Cloning_settings_copies_every_field()
        {
            var settings = new AppSettings { GroupWidth = 8, LoggingEnabled = false };

            AppSettings clone = settings.Clone();

            Assert.Equal(8, clone.GroupWidth);
            Assert.False(clone.LoggingEnabled);
            Assert.NotSame(settings, clone);
        }
    }

    /// <summary>Icon metadata.</summary>
    public class IconRepositoryTests : IDisposable
    {
        private readonly TestDataDirectory _directory = new TestDataDirectory("icons");
        private readonly Database _database;
        private readonly IconRepository _icons;

        public IconRepositoryTests()
        {
            _database = new Database(_directory.File("TaskbarGroups.db"));
            SchemaMigrator.Initialize(_database);
            _icons = new IconRepository(_database);
        }

        public void Dispose()
        {
            _database.Dispose();
            _directory.Dispose();
        }

        [Fact]
        public void An_icon_record_round_trips()
        {
            _icons.Upsert("key1", @"C:\icons\a.png", "fingerprint", 64, 2048, DateTimeOffset.UtcNow);

            IconRecord? record = _icons.Get("key1");

            Assert.NotNull(record);
            Assert.Equal(@"C:\icons\a.png", record!.FilePath);
            Assert.Equal("fingerprint", record.SourceFingerprint);
            Assert.Equal(64, record.PixelSize);
            Assert.Equal(2048, record.ByteLength);
        }

        [Fact]
        public void Upserting_the_same_key_twice_updates_it()
        {
            _icons.Upsert("key1", @"C:\icons\a.png", "old", 32, 1, DateTimeOffset.UtcNow);
            _icons.Upsert("key1", @"C:\icons\b.png", "new", 64, 2, DateTimeOffset.UtcNow);

            IconRecord record = _icons.GetAll().Single();

            Assert.Equal(@"C:\icons\b.png", record.FilePath);
            Assert.Equal("new", record.SourceFingerprint);
            Assert.Equal(64, record.PixelSize);
        }

        [Fact]
        public void A_missing_key_returns_null()
        {
            Assert.Null(_icons.Get("nothing"));
            Assert.Null(_icons.Get(""));
        }

        [Fact]
        public void Deleting_removes_one_record_and_deleting_all_empties_the_table()
        {
            _icons.Upsert("a", @"C:\a.png", "f", 32, 1, DateTimeOffset.UtcNow);
            _icons.Upsert("b", @"C:\b.png", "f", 32, 1, DateTimeOffset.UtcNow);

            Assert.Equal(1, _icons.Delete("a"));
            Assert.Single(_icons.GetAll());

            Assert.Equal(1, _icons.DeleteAll());
            Assert.Empty(_icons.GetAll());
        }

        [Fact]
        public void An_empty_key_is_ignored()
        {
            _icons.Upsert("", @"C:\a.png", "f", 32, 1, DateTimeOffset.UtcNow);

            Assert.Empty(_icons.GetAll());
        }
    }

    /// <summary>The migration log.</summary>
    public class MigrationHistoryTests : IDisposable
    {
        private readonly TestDataDirectory _directory = new TestDataDirectory("history");
        private readonly Database _database;
        private readonly MigrationHistoryRepository _history;

        public MigrationHistoryTests()
        {
            _database = new Database(_directory.File("TaskbarGroups.db"));
            SchemaMigrator.Initialize(_database);
            _history = new MigrationHistoryRepository(_database);
        }

        public void Dispose()
        {
            _database.Dispose();
            _directory.Dispose();
        }

        [Fact]
        public void A_completed_migration_is_recorded_and_recognised()
        {
            _history.Record("legacy-xml", 1, "Completed", 4, 27, @"C:\report.json", @"C:\backup.zip",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

            Assert.True(_history.HasCompleted("legacy-xml"));
        }

        [Fact]
        public void A_failed_migration_does_not_count_as_completed()
        {
            // This is what lets a failed import be retried rather than being
            // permanently skipped.
            _history.Record("legacy-xml", 1, "Failed", 0, 0, string.Empty, @"C:\backup.zip",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

            Assert.False(_history.HasCompleted("legacy-xml"));
        }

        [Fact]
        public void An_incomplete_migration_does_not_count_as_completed()
        {
            _history.Record("legacy-xml", 1, "Partial", 2, 9, string.Empty, string.Empty,
                DateTimeOffset.UtcNow, null);

            Assert.False(_history.HasCompleted("legacy-xml"));
        }

        [Fact]
        public void History_is_returned_newest_first()
        {
            _history.Record("source", 1, "Completed", 1, 1, string.Empty, string.Empty,
                DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-2));

            _history.Record("source", 1, "Completed", 2, 2, string.Empty, string.Empty,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

            List<(DateTimeOffset StartedAt, string Status, int Groups, string BackupPath)> all =
                _history.GetAll().ToList();

            Assert.Equal(2, all.Count);
            Assert.True(all[0].StartedAt >= all[1].StartedAt);
        }

        [Fact]
        public void Different_sources_are_tracked_independently()
        {
            _history.Record("legacy-xml", 1, "Completed", 1, 1, string.Empty, string.Empty,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

            Assert.True(_history.HasCompleted("legacy-xml"));
            Assert.False(_history.HasCompleted("something-else"));
        }
    }

    /// <summary>Thin SQL helper, so assertions can look at the schema directly.</summary>
    internal sealed class SqliteProbe : IDisposable
    {
        private SqliteProbe(int count)
        {
            Count = count;
        }

        public int Count { get; }

        public static SqliteProbe Query(Database database, string sql, params (string Name, object? Value)[] parameters)
        {
            using SqliteCommand command = database.CreateCommand(sql, parameters);
            object? value = command.ExecuteScalar();
            return new SqliteProbe(value == null ? 0 : Convert.ToInt32(value));
        }

        public static SqliteProbe Execute(Database database, string sql, params (string Name, object? Value)[] parameters)
        {
            using SqliteCommand command = database.CreateCommand(sql, parameters);
            return new SqliteProbe(command.ExecuteNonQuery());
        }

        public void Dispose()
        {
        }
    }
}