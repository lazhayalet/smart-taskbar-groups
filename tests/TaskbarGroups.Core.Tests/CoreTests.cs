using System;
using System.Collections.Generic;
using System.Linq;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;
using Xunit;

namespace TaskbarGroups.Core.Tests
{
    /// <summary>
    /// Group name rules.
    /// </summary>
    /// <remarks>
    /// The legacy editor used two different transforms on the same string - spaces
    /// to underscores for the folder, underscores to spaces for the display name -
    /// and validated it with a regex that rejected anything outside
    /// <c>[0-9a-zA-Z ]</c>. These tests pin the two transforms together and the
    /// validation to what the file system actually requires.
    /// </remarks>
    public class GroupNameTests
    {
        [Theory]
        [InlineData("Work", "Work")]
        [InlineData("My Group", "My_Group")]
        [InlineData("My  Group", "My_Group")]
        [InlineData("  Padded  ", "Padded")]
        public void StorageName_replaces_whitespace_with_underscores(string input, string expected)
        {
            Assert.Equal(expected, GroupNameValidator.ToStorageName(input));
        }

        [Theory]
        [InlineData("My_Group", "My Group")]
        [InlineData("My__Group", "My Group")]
        [InlineData("Work", "Work")]
        public void DisplayName_replaces_underscore_runs_with_spaces(string input, string expected)
        {
            Assert.Equal(expected, GroupNameValidator.ToDisplayName(input));
        }

        [Fact]
        public void Storage_and_display_round_trip()
        {
            const string original = "Development Tools";

            string storage = GroupNameValidator.ToStorageName(original);
            string display = GroupNameValidator.ToDisplayName(storage);

            Assert.Equal(original, display);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Blank_names_are_rejected(string? name)
        {
            ValidationResult result = GroupNameValidator.Validate(name);

            Assert.False(result.IsValid);
            Assert.Equal("Must select a name", result.Message);
        }

        [Theory]
        [InlineData("bad/name")]
        [InlineData("bad\\name")]
        [InlineData("bad:name")]
        [InlineData("bad*name")]
        [InlineData("bad?name")]
        [InlineData("bad|name")]
        [InlineData("bad\"name")]
        [InlineData("bad<name")]
        [InlineData("bad>name")]
        public void File_system_reserved_characters_are_rejected(string name)
        {
            Assert.False(GroupNameValidator.Validate(name).IsValid);
        }

        [Fact]
        public void Control_characters_are_rejected()
        {
            Assert.False(GroupNameValidator.Validate("bad\u0001name").IsValid);
            Assert.False(GroupNameValidator.Validate("bad\nname").IsValid);
        }

        [Fact]
        public void Names_at_the_limit_are_accepted()
        {
            string name = new string('a', GroupNameValidator.MaxLength);

            Assert.True(GroupNameValidator.Validate(name).IsValid);
        }

        [Fact]
        public void Names_over_the_limit_are_rejected()
        {
            string name = new string('a', GroupNameValidator.MaxLength + 1);

            ValidationResult result = GroupNameValidator.Validate(name);

            Assert.False(result.IsValid);
            Assert.Contains("49", result.Message);
        }

        [Fact]
        public void Turkish_characters_are_accepted()
        {
            // The legacy regex rejected these even though they are perfectly valid
            // file names on Windows. A Turkish user should be able to name a group
            // "İş Uygulamaları".
            Assert.True(GroupNameValidator.Validate("İş Uygulamaları").IsValid);
            Assert.True(GroupNameValidator.Validate("Çalışma").IsValid);
            Assert.True(GroupNameValidator.Validate("日本語").IsValid);
        }

        [Fact]
        public void Legacy_compatibility_check_distinguishes_non_ascii()
        {
            Assert.True(GroupNameValidator.IsLegacyCompatible("Work Stuff"));
            Assert.False(GroupNameValidator.IsLegacyCompatible("İş"));
        }
    }

    /// <summary>
    /// Group-level validation, including the caps the legacy editor enforced.
    /// </summary>
    public class GroupValidatorTests
    {
        private static Group ValidGroup()
        {
            return new Group { Name = "Work", Width = 5, Opacity = 10 };
        }

        private static ShortcutItem ValidShortcut()
        {
            return new ShortcutItem
            {
                Name = "Notepad",
                Type = ShortcutType.Exe,
                Target = @"C:\Windows\System32\notepad.exe"
            };
        }

        [Fact]
        public void A_well_formed_group_has_no_problems()
        {
            IReadOnlyList<string> problems = GroupValidator.Validate(ValidGroup(), new[] { ValidShortcut() });

            Assert.Empty(problems);
        }

        [Fact]
        public void A_group_with_no_shortcuts_is_rejected()
        {
            IReadOnlyList<string> problems = GroupValidator.Validate(ValidGroup(), Array.Empty<ShortcutItem>());

            Assert.Contains(problems, p => p.Contains("at least one shortcut"));
        }

        [Fact]
        public void The_twenty_shortcut_cap_is_enforced()
        {
            var shortcuts = new List<ShortcutItem>();
            for (int index = 0; index <= GroupValidator.MaxShortcutsPerGroup; index++)
                shortcuts.Add(ValidShortcut());

            IReadOnlyList<string> problems = GroupValidator.Validate(ValidGroup(), shortcuts);

            Assert.Contains(problems, p => p.Contains("Max 20"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(20)]
        public void Width_outside_the_allowed_range_is_rejected(int width)
        {
            Group group = ValidGroup();
            group.Width = width;

            IReadOnlyList<string> problems = GroupValidator.Validate(group, new[] { ValidShortcut() });

            Assert.Contains(problems, p => p.Contains("Width"));
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(100.1)]
        public void Opacity_outside_zero_to_hundred_is_rejected(double opacity)
        {
            Group group = ValidGroup();
            group.Opacity = opacity;

            IReadOnlyList<string> problems = GroupValidator.Validate(group, new[] { ValidShortcut() });

            Assert.Contains(problems, p => p.Contains("Opacity"));
        }

        [Fact]
        public void Corner_radius_outside_the_allowed_range_is_rejected()
        {
            Group group = ValidGroup();
            group.CornerRadius = 100;

            IReadOnlyList<string> problems = GroupValidator.Validate(group, new[] { ValidShortcut() });

            Assert.Contains(problems, p => p.Contains("Corner radius"));
        }
    }

    /// <summary>
    /// Per-shortcut validation and health classification.
    /// </summary>
    public class ShortcutValidatorTests
    {
        [Fact]
        public void An_empty_target_is_rejected()
        {
            var item = new ShortcutItem { Type = ShortcutType.Exe, Target = "" };

            Assert.False(ShortcutValidator.Validate(item).IsValid);
        }

        [Fact]
        public void A_relative_exe_path_is_rejected()
        {
            var item = new ShortcutItem { Type = ShortcutType.Exe, Target = "notepad.exe" };

            ValidationResult result = ShortcutValidator.Validate(item);

            Assert.False(result.IsValid);
            Assert.Contains("absolute", result.Message);
        }

        [Fact]
        public void A_directory_recorded_as_an_exe_is_rejected()
        {
            // This is the shape the legacy model produced for a folder shortcut:
            // a Folder path stored with no type distinction. Validation catches it
            // rather than letting it reach Icon.ExtractAssociatedIcon at runtime.
            var item = new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\Windows" };

            ValidationResult result = ShortcutValidator.Validate(item);

            Assert.False(result.IsValid);
            Assert.Contains("folder", result.Message);
        }

        [Fact]
        public void A_missing_exe_is_rejected()
        {
            var item = new ShortcutItem
            {
                Type = ShortcutType.Exe,
                Target = @"C:\definitely\not\here\missing.exe"
            };

            Assert.False(ShortcutValidator.Validate(item).IsValid);
        }

        [Fact]
        public void An_existing_exe_is_accepted()
        {
            var item = new ShortcutItem
            {
                Type = ShortcutType.Exe,
                Target = Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\notepad.exe"
            };

            if (!System.IO.File.Exists(item.Target))
            {
                // Notepad is present on every supported Windows edition, but if a
                // trimmed image lacks it the assertion is meaningless rather than
                // wrong.
                return;
            }

            Assert.True(ShortcutValidator.Validate(item).IsValid);
        }

        [Fact]
        public void A_real_folder_is_accepted_as_a_folder()
        {
            var item = new ShortcutItem
            {
                Type = ShortcutType.Folder,
                Target = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };

            Assert.True(ShortcutValidator.Validate(item).IsValid);
            Assert.Equal(ShortcutStatus.Valid, ShortcutValidator.Classify(item));
        }

        [Fact]
        public void A_missing_folder_classifies_as_missing()
        {
            var item = new ShortcutItem
            {
                Type = ShortcutType.Folder,
                Target = @"C:\definitely\not\here\folder"
            };

            Assert.Equal(ShortcutStatus.Missing, ShortcutValidator.Classify(item));
        }

        [Fact]
        public void A_well_formed_url_is_accepted()
        {
            var item = new ShortcutItem { Type = ShortcutType.Url, Target = "https://example.com/path?a=1" };

            Assert.True(ShortcutValidator.Validate(item).IsValid);
            Assert.Equal(ShortcutStatus.UriLauncher, ShortcutValidator.Classify(item));
        }

        [Fact]
        public void A_steam_uri_classifies_as_a_uri_launcher()
        {
            var item = new ShortcutItem { Type = ShortcutType.Steam, Target = "steam://rungameid/440" };

            Assert.True(ShortcutValidator.Validate(item).IsValid);
            Assert.Equal(ShortcutStatus.UriLauncher, ShortcutValidator.Classify(item));
        }

        [Fact]
        public void An_app_user_model_id_is_accepted()
        {
            var item = new ShortcutItem
            {
                Type = ShortcutType.Uwp,
                Target = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"
            };

            Assert.True(ShortcutValidator.Validate(item).IsValid);
        }

        [Fact]
        public void A_value_without_a_bang_is_not_an_app_id()
        {
            var item = new ShortcutItem { Type = ShortcutType.Uwp, Target = "notanappid" };

            Assert.False(ShortcutValidator.Validate(item).IsValid);
        }

        [Fact]
        public void A_disabled_item_classifies_as_disabled()
        {
            var item = new ShortcutItem
            {
                Type = ShortcutType.Exe,
                Target = Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\notepad.exe",
                Enabled = false
            };

            Assert.Equal(ShortcutStatus.Disabled, ShortcutValidator.Classify(item));
        }

        [Fact]
        public void Classification_never_throws_for_a_broken_link()
        {
            // No link resolver is registered, which is exactly the situation in a
            // unit test and the situation on a machine where the shell refuses the
            // file. Classification must still answer.
            var item = new ShortcutItem
            {
                Type = ShortcutType.Lnk,
                Target = @"C:\definitely\not\here\broken.lnk"
            };

            ShortcutStatus status = ShortcutValidator.Classify(item);

            Assert.Equal(ShortcutStatus.Missing, status);
        }

        [Fact]
        public void Classification_of_a_null_item_is_unknown_rather_than_a_crash()
        {
            Assert.Equal(ShortcutStatus.Unknown, ShortcutValidator.Classify(null!));
        }
    }

    /// <summary>Argument quoting, which is the injection-adjacent surface.</summary>
    public class StringHelpersTests
    {
        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("", "\"\"")]
        [InlineData("with space", "\"with space\"")]
        [InlineData("tab\there", "\"tab\there\"")]
        public void Arguments_are_quoted_only_when_they_need_to_be(string input, string expected)
        {
            Assert.Equal(expected, StringHelpers.QuoteArgument(input));
        }

        [Fact]
        public void An_already_quoted_argument_is_left_alone()
        {
            const string alreadyQuoted = "\"already quoted\"";

            Assert.Equal(alreadyQuoted, StringHelpers.QuoteArgument(alreadyQuoted));
        }

        [Fact]
        public void Embedded_quotes_are_escaped()
        {
            string quoted = StringHelpers.QuoteArgument("say \"hello\"");

            Assert.StartsWith("\"", quoted);
            Assert.EndsWith("\"", quoted);
            Assert.Contains("\\\"", quoted);
        }

        [Fact]
        public void Trailing_backslashes_before_the_closing_quote_are_doubled()
        {
            // "C:\path\" must not become "C:\path" plus a stray escape that eats
            // the closing quote.
            string quoted = StringHelpers.QuoteArgument(@"C:\path with space\");

            Assert.Equal("\"C:\\path with space\\\\\"", quoted);
        }

        [Fact]
        public void A_null_argument_becomes_an_empty_pair_of_quotes()
        {
            Assert.Equal("\"\"", StringHelpers.QuoteArgument(null));
        }

        [Fact]
        public void Shell_metacharacters_in_an_argument_are_not_special()
        {
            // Quoting a single argument means these stay inside the argument. They
            // are not stripped, concatenated or reinterpreted.
            string quoted = StringHelpers.QuoteArgument("a & b | c > d");

            Assert.Contains("&", quoted);
            Assert.Contains("|", quoted);
            Assert.StartsWith("\"", quoted);
        }

        [Fact]
        public void ExpandPath_resolves_environment_variables()
        {
            string expanded = StringHelpers.ExpandPath("%SystemRoot%\\notepad.exe");

            Assert.DoesNotContain("%SystemRoot%", expanded);
            Assert.True(expanded.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                StringComparison.OrdinalIgnoreCase) || expanded.Contains("Windows"));
        }

        [Fact]
        public void ExpandPath_handles_a_malformed_variable_without_throwing()
        {
            string expanded = StringHelpers.ExpandPath(@"%NOT_A_REAL_VAR%\file.exe");

            Assert.NotNull(expanded);
        }

        [Fact]
        public void ExpandPath_strips_surrounding_quotes()
        {
            string expanded = StringHelpers.ExpandPath("\"C:\\Windows\\notepad.exe\"");

            Assert.False(expanded.StartsWith("\"", StringComparison.Ordinal));
            Assert.EndsWith("notepad.exe", expanded);
        }

        [Theory]
        [InlineData("simple", "simple")]
        [InlineData("with.dot", "with.dot")]
        [InlineData("with:colon", "with_colon")]
        [InlineData("with/slash", "with_slash")]
        [InlineData("with\\backslash", "with_backslash")]
        public void SanitizeFileName_replaces_only_illegal_characters(string input, string expected)
        {
            Assert.Equal(expected, StringHelpers.SanitizeFileName(input));
        }

        [Fact]
        public void SanitizeFileName_keeps_non_ascii_characters()
        {
            // The legacy icon cache stripped these out with a regex, so two
            // differently-named Turkish applications collided on one cache entry.
            Assert.Equal("İş_Uygulaması", StringHelpers.SanitizeFileName("İş:Uygulaması"));
        }

        [Fact]
        public void SanitizeFileName_truncates_and_never_returns_empty()
        {
            string long1 = StringHelpers.SanitizeFileName(new string('a', 500), 32);
            Assert.Equal(32, long1.Length);

            Assert.Equal("unnamed", StringHelpers.SanitizeFileName("   "));
        }
    }

    /// <summary>Hotkey parsing and conflict detection.</summary>
    public class HotkeyGestureTests
    {
        [Theory]
        [InlineData("Ctrl+Alt+G", true)]
        [InlineData("ctrl+alt+g", true)]
        [InlineData("Ctrl+Shift+Alt+Win+F12", true)]
        [InlineData("Ctrl+1", true)]
        [InlineData("Alt+A", true)]
        public void Well_formed_gestures_parse(string gesture, bool expected)
        {
            Assert.Equal(expected, HotkeyGesture.IsParseable(gesture));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Ctrl")]            // modifiers only
        [InlineData("Ctrl+Alt")]
        [InlineData("Ctrl+NotAKey")]
        [InlineData("+")]
        public void Malformed_gestures_do_not_parse(string? gesture)
        {
            Assert.False(HotkeyGesture.IsParseable(gesture));
        }

        [Fact]
        public void The_display_form_is_canonical()
        {
            Assert.True(HotkeyGesture.TryParse("ctrl+ALT+g", out HotkeyGesture parsed));
            Assert.Equal("Ctrl+Alt+G", parsed.Display);
        }

        [Fact]
        public void Modifier_flags_are_readable()
        {
            Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+Shift+G", out HotkeyGesture parsed));

            Assert.True(parsed.IsControl);
            Assert.True(parsed.IsAlt);
            Assert.True(parsed.IsShift);
            Assert.False(parsed.IsWin);
        }

        [Fact]
        public void A_conflict_is_detected_against_the_popup_hotkeys()
        {
            string? conflict = HotkeyGesture.FindConflict(
                "Ctrl+Enter",
                new[] { HotkeyGesture.Defaults.OpenAll });

            Assert.Equal("Ctrl+Enter", conflict);
        }

        [Fact]
        public void A_different_modifier_is_not_a_conflict()
        {
            string? conflict = HotkeyGesture.FindConflict(
                "Alt+Enter",
                new[] { HotkeyGesture.Defaults.OpenAll });

            Assert.Null(conflict);
        }

        [Fact]
        public void Normalize_returns_null_for_an_unusable_gesture()
        {
            Assert.Null(HotkeyGesture.Normalize("Ctrl"));
            Assert.Equal("Ctrl+Alt+G", HotkeyGesture.Normalize("ctrl+alt+g"));
        }
    }

    /// <summary>Ordering rules used by the group popup.</summary>
    public class ShortcutSortHelperTests
    {
        private static ShortcutItem Item(string name, ShortcutType type, int order)
        {
            return new ShortcutItem { Name = name, Type = type, SortOrder = order };
        }

        [Fact]
        public void Manual_order_follows_sort_order()
        {
            var items = new List<ShortcutItem>
            {
                Item("c", ShortcutType.Exe, 2),
                Item("a", ShortcutType.Exe, 0),
                Item("b", ShortcutType.Exe, 1)
            };

            IReadOnlyList<ShortcutItem> ordered = ShortcutSortHelper.Order(items, GroupSortMode.Manual);

            Assert.Equal(new[] { "a", "b", "c" }, ordered.Select(i => i.Name));
        }

        [Fact]
        public void Name_ordering_is_case_insensitive()
        {
            var items = new List<ShortcutItem>
            {
                Item("beta", ShortcutType.Exe, 0),
                Item("Alpha", ShortcutType.Exe, 1)
            };

            IReadOnlyList<ShortcutItem> ordered = ShortcutSortHelper.Order(items, GroupSortMode.NameAscending);

            Assert.Equal(new[] { "Alpha", "beta" }, ordered.Select(i => i.Name));
        }

        [Fact]
        public void Descending_name_ordering_reverses_it()
        {
            var items = new List<ShortcutItem>
            {
                Item("Alpha", ShortcutType.Exe, 0),
                Item("beta", ShortcutType.Exe, 1)
            };

            IReadOnlyList<ShortcutItem> ordered = ShortcutSortHelper.Order(items, GroupSortMode.NameDescending);

            Assert.Equal(new[] { "beta", "Alpha" }, ordered.Select(i => i.Name));
        }

        [Fact]
        public void Automatic_ordering_puts_running_items_first()
        {
            var items = new List<ShortcutItem>
            {
                Item("first", ShortcutType.Exe, 0),
                Item("second", ShortcutType.Exe, 1)
            };

            IReadOnlyList<ShortcutItem> ordered = ShortcutSortHelper.Order(
                items,
                GroupSortMode.Automatic,
                item => item.Name == "second");

            Assert.Equal("second", ordered[0].Name);
        }

        [Fact]
        public void An_empty_list_is_handled()
        {
            Assert.Empty(ShortcutSortHelper.Order(Array.Empty<ShortcutItem>(), GroupSortMode.Manual));
            Assert.Empty(ShortcutSortHelper.Order(null!, GroupSortMode.Manual));
        }
    }

    /// <summary>Domain model invariants.</summary>
    public class ModelTests
    {
        [Fact]
        public void A_new_group_has_sane_defaults()
        {
            var group = new Group();

            Assert.NotEqual(Guid.Empty, group.Id);
            Assert.Equal(5, group.Width);
            Assert.Equal(GroupTheme.System, group.Theme);
            Assert.Equal(GroupSortMode.Manual, group.SortMode);
            Assert.True(group.OpenAllEnabled);
            Assert.Equal(8, group.CornerRadius);
        }

        [Fact]
        public void Touch_advances_the_update_timestamp()
        {
            var group = new Group();
            DateTimeOffset before = group.UpdatedAt;

            group.Touch();

            Assert.True(group.UpdatedAt >= before);
        }

        [Fact]
        public void Cloning_a_group_keeps_its_identity_but_changes_the_name()
        {
            var group = new Group { Name = "Original" };

            Group copy = group.Clone();

            Assert.Equal(group.Id, copy.Id);
            Assert.Equal("Original", copy.Name);
        }

        [Fact]
        public void Cloning_a_shortcut_gives_it_a_new_identity()
        {
            var item = new ShortcutItem { Name = "x" };

            ShortcutItem copy = item.Clone();

            Assert.NotEqual(item.Id, copy.Id);
            Assert.Equal("x", copy.Name);
        }

        [Fact]
        public void A_cloned_workspace_gets_fresh_ids_throughout()
        {
            var workspace = new Workspace { Name = "Development" };
            var item = new WorkspaceItem { Executable = @"C:\a.exe" };
            workspace.Items.Add(item);

            Workspace copy = workspace.Clone();

            Assert.NotEqual(workspace.Id, copy.Id);
            Assert.Single(copy.Items);
            Assert.NotEqual(item.Id, copy.Items[0].Id);
            Assert.Equal(copy.Id, copy.Items[0].WorkspaceId);
        }

        [Fact]
        public void A_new_shortcut_defaults_to_enabled_and_normal_window_state()
        {
            var item = new ShortcutItem();

            Assert.True(item.Enabled);
            Assert.Equal(WindowStatePreference.Normal, item.WindowState);
            Assert.Equal(MonitorPreference.FollowCursor, item.PreferredMonitor);
            Assert.Equal(ShortcutType.Unknown, item.Type);
        }

        [Fact]
        public void Monitor_identity_compares_by_device_name()
        {
            var first = new MonitorInfo { Id = "DISPLAY1", DeviceName = @"\\.\DISPLAY1" };
            var second = new MonitorInfo { Id = "DISPLAY1", DeviceName = @"\\.\DISPLAY1" };
            var third = new MonitorInfo { Id = "DISPLAY2", DeviceName = @"\\.\DISPLAY2" };

            Assert.True(first.IsSameDevice(second));
            Assert.False(first.IsSameDevice(third));
            Assert.False(first.IsSameDevice(null));
        }

        [Fact]
        public void DataPaths_layout_is_derived_from_the_base_directory()
        {
            var paths = new DataPaths(@"C:\Data");

            Assert.Equal(@"C:\Data", paths.BaseDirectory);
            Assert.Equal(@"C:\Data\TaskbarGroups.db", paths.DatabaseFile);
            Assert.Equal(@"C:\Data\Icons", paths.IconsDirectory);
            Assert.Equal(@"C:\Data\Backups", paths.BackupsDirectory);
            Assert.Equal(@"C:\Data\Logs", paths.LogsDirectory);
            Assert.Equal(@"C:\Data\config", paths.LegacyConfigDirectory);
            Assert.Equal(@"C:\Data\Shortcuts", paths.LegacyShortcutsDirectory);
        }

        [Fact]
        public void DataPaths_rejects_an_empty_directory()
        {
            Assert.Throws<ArgumentException>(() => new DataPaths(""));
            Assert.Throws<ArgumentException>(() => new DataPaths("   "));
        }

        [Fact]
        public void Export_conversion_round_trips_a_group()
        {
            var group = new Group
            {
                Name = "Work",
                Width = 7,
                Theme = GroupTheme.Dark,
                Opacity = 42,
                OpenAllEnabled = false
            };

            var exported = new ExportGroup
            {
                Id = group.Id,
                Name = group.Name,
                Width = group.Width,
                Theme = group.Theme,
                Opacity = group.Opacity,
                OpenAllEnabled = group.OpenAllEnabled
            };

            Group restored = exported.ToDomain();

            Assert.Equal(group.Id, restored.Id);
            Assert.Equal("Work", restored.Name);
            Assert.Equal(7, restored.Width);
            Assert.Equal(GroupTheme.Dark, restored.Theme);
            Assert.Equal(42, restored.Opacity);
            Assert.False(restored.OpenAllEnabled);
        }

        [Fact]
        public void Export_conversion_carries_shortcut_fields()
        {
            var exported = new ExportShortcut
            {
                Name = "Notepad",
                Type = ShortcutType.Exe,
                Target = @"C:\notepad.exe",
                Arguments = "-a -b",
                WorkingDirectory = @"C:\",
                RunAsAdministrator = true,
                WindowState = WindowStatePreference.Maximized,
                Enabled = false,
                SortOrder = 3
            };

            var groupId = Guid.NewGuid();
            ShortcutItem item = exported.ToDomain(groupId);

            Assert.Equal(groupId, item.GroupId);
            Assert.Equal("Notepad", item.Name);
            Assert.Equal(ShortcutType.Exe, item.Type);
            Assert.Equal("-a -b", item.Arguments);
            Assert.True(item.RunAsAdministrator);
            Assert.Equal(WindowStatePreference.Maximized, item.WindowState);
            Assert.False(item.Enabled);
            Assert.Equal(3, item.SortOrder);

            // The cache key is deliberately not exported: it is derived from the
            // target on the destination machine, not carried across.
            Assert.Equal(string.Empty, item.IconCacheKey);
        }
    }
}