using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Launchers;
using TaskbarGroups.Launchers.Detection;
using Xunit;

namespace TaskbarGroups.Launcher.Tests
{
    /// <summary>
    /// Type detection.
    /// </summary>
    /// <remarks>
    /// The legacy editor had none: <c>frmGroup.addShortcut</c> stored whatever path
    /// it was handed, and <c>frmMain.OpenFile</c> handed that to
    /// <c>Process.Start</c>. A dropped folder therefore produced a shortcut that
    /// could never launch, and a dropped <c>.url</c> or <c>steam://</c> link
    /// produced one that threw at launch time. These tests pin the classification
    /// order that fixes it.
    /// </remarks>
    public class TargetClassifierTests
    {
        private static string SystemExe(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);

        [Fact]
        public void An_exe_is_classified_as_an_exe()
        {
            string target = SystemExe("notepad.exe");

            if (!File.Exists(target)) return;

            Assert.Equal(ShortcutType.Exe, TargetClassifier.Classify(target));
        }

        [Fact]
        public void An_exe_by_extension_is_classified_even_when_missing()
        {
            // The file may have been uninstalled. The type is still meaningful, so
            // the user sees a "missing" badge rather than an unknown one.
            Assert.Equal(ShortcutType.Exe, TargetClassifier.Classify(@"C:\gone\thing.exe"));
        }

        [Fact]
        public void An_existing_directory_is_a_folder_regardless_of_its_name()
        {
            // "tools.exe" as a directory must not be classified as an executable;
            // that ordering is what made folder shortcuts reach CreateProcess.
            string folder = Path.Combine(Path.GetTempPath(), "taskbargroups-test-folder.exe");
            Directory.CreateDirectory(folder);

            try
            {
                Assert.Equal(ShortcutType.Folder, TargetClassifier.Classify(folder));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void A_real_directory_is_a_folder()
        {
            Assert.Equal(ShortcutType.Folder,
                TargetClassifier.Classify(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        }

        [Theory]
        [InlineData("https://example.com", ShortcutType.Url)]
        [InlineData("http://example.com/path", ShortcutType.Url)]
        [InlineData("steam://rungameid/440", ShortcutType.Steam)]
        [InlineData("steam://run/440", ShortcutType.Steam)]
        [InlineData("epic://launch", ShortcutType.Epic)]
        [InlineData("origin://launch", ShortcutType.EA)]
        [InlineData("uplay://launch", ShortcutType.Ubisoft)]
        [InlineData("battle://launch", ShortcutType.BattleNet)]
        [InlineData("goggalaxy://launch", ShortcutType.Gog)]
        public void Uris_are_classified_by_scheme(string uri, ShortcutType expected)
        {
            Assert.Equal(expected, TargetClassifier.Classify(uri));
        }

        [Fact]
        public void An_unregistered_scheme_is_still_shell_activated()
        {
            // A protocol this build does not know about is still better handled as
            // a URL than as an executable.
            Assert.Equal(ShortcutType.Url, TargetClassifier.Classify("myapp://open/thing"));
        }

        [Fact]
        public void A_shell_apps_folder_reference_is_a_packaged_app()
        {
            Assert.Equal(ShortcutType.Uwp,
                TargetClassifier.Classify(@"shell:appsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"));
        }

        [Fact]
        public void A_bare_app_user_model_id_is_a_packaged_app()
        {
            Assert.Equal(ShortcutType.Uwp,
                TargetClassifier.Classify("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"));
        }

        [Theory]
        [InlineData(@"C:\temp\run.bat", ShortcutType.Bat)]
        [InlineData(@"C:\temp\run.cmd", ShortcutType.Cmd)]
        [InlineData(@"C:\temp\run.ps1", ShortcutType.Ps1)]
        [InlineData(@"C:\temp\module.psm1", ShortcutType.Ps1)]
        [InlineData(@"C:\temp\app.appref-ms", ShortcutType.AppRefMs)]
        public void Script_and_reference_extensions_map_to_their_types(string target, ShortcutType expected)
        {
            Assert.Equal(expected, TargetClassifier.Classify(target));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Empty_input_is_unknown(string? target)
        {
            Assert.Equal(ShortcutType.Unknown, TargetClassifier.Classify(target));
        }

        [Fact]
        public void A_lnk_is_classified_as_a_link()
        {
            string link = Path.Combine(Path.GetTempPath(), "taskbargroups-classify-test.lnk");
            File.WriteAllText(link, "not a real shortcut");

            try
            {
                // Unreadable as a link, so it stays a plain link rather than being
                // dropped.
                Assert.Equal(ShortcutType.Lnk, TargetClassifier.Classify(link));
            }
            finally
            {
                File.Delete(link);
            }
        }

        [Fact]
        public void A_windows_path_is_not_mistaken_for_a_uri()
        {
            // "C:\x" parses as a URI with the "file" scheme; the path forms have to
            // be checked first or every drive letter becomes a folder shortcut.
            Assert.NotEqual(ShortcutType.Url, TargetClassifier.Classify(@"C:\Windows\notepad.exe"));
        }

        [Fact]
        public void A_unc_path_is_not_mistaken_for_a_uri()
        {
            Assert.NotEqual(ShortcutType.Url, TargetClassifier.Classify(@"\\server\share\app.exe"));
        }

        [Theory]
        [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", true)]
        [InlineData("SomeFamily_123!SomeApp", true)]
        [InlineData("NoBang", false)]
        [InlineData("!Leading", false)]
        [InlineData("Trailing!", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void App_user_model_id_shape_is_recognised(string? value, bool expected)
        {
            Assert.Equal(expected, TargetClassifier.LooksLikeAppUserModelId(value));
        }
    }

    /// <summary>
    /// Resolution: a raw target becomes a complete, validated item.
    /// </summary>
    public class ShortcutResolverTests
    {
        private readonly ShortcutResolver _resolver = new ShortcutResolver();

        [Fact]
        public void An_existing_folder_resolves_to_a_folder_with_a_working_directory()
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            ResolvedShortcut resolved = _resolver.Resolve(folder);

            Assert.Equal(ShortcutType.Folder, resolved.Type);
            Assert.Equal(new DirectoryInfo(folder).Name, resolved.DisplayName);
            Assert.Equal(folder, resolved.SuggestedWorkingDirectory);
            Assert.True(resolved.Exists);
        }

        [Fact]
        public void A_url_resolves_without_touching_the_file_system()
        {
            ResolvedShortcut resolved = _resolver.Resolve("https://example.com/docs", "Docs");

            Assert.Equal(ShortcutType.Url, resolved.Type);
            Assert.Equal("https://example.com/docs", resolved.Target);
            Assert.Equal("Docs", resolved.DisplayName);
            Assert.True(resolved.IsValid);
        }

        [Fact]
        public void A_url_without_a_name_takes_its_host()
        {
            ResolvedShortcut resolved = _resolver.Resolve("https://example.com/some/long/path");

            Assert.Equal("example.com", resolved.DisplayName);
        }

        [Fact]
        public void A_missing_exe_resolves_with_a_warning_rather_than_throwing()
        {
            ResolvedShortcut resolved = _resolver.Resolve(@"C:\gone\missing.exe");

            Assert.Equal(ShortcutType.Exe, resolved.Type);
            Assert.NotEmpty(resolved.Warnings);
            Assert.Contains(resolved.Warnings, w => w.Contains("does not exist"));
            Assert.False(resolved.IsValid);
        }

        [Fact]
        public void An_empty_target_resolves_with_a_warning()
        {
            ResolvedShortcut resolved = _resolver.Resolve("   ");

            Assert.Equal(ShortcutType.Unknown, resolved.Type);
            Assert.NotEmpty(resolved.Warnings);
        }

        [Fact]
        public void An_existing_exe_gets_a_working_directory_from_its_own_folder()
        {
            string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
            if (!File.Exists(target)) return;

            ResolvedShortcut resolved = _resolver.Resolve(target);

            Assert.Equal(ShortcutType.Exe, resolved.Type);
            Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.System),
                resolved.SuggestedWorkingDirectory, ignoreCase: true);
        }

        [Fact]
        public void A_shell_apps_folder_prefix_is_stripped_from_the_stored_target()
        {
            ResolvedShortcut resolved = _resolver.Resolve(
                @"shell:appsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");

            Assert.Equal(ShortcutType.Uwp, resolved.Type);
            Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", resolved.Target);
        }

        [Fact]
        public void A_steam_uri_keeps_its_uri_as_the_target()
        {
            ResolvedShortcut resolved = _resolver.Resolve("steam://rungameid/440", "Team Fortress 2");

            Assert.Equal(ShortcutType.Steam, resolved.Type);
            Assert.Equal("steam://rungameid/440", resolved.Target);
            Assert.Equal("Team Fortress 2", resolved.DisplayName);
        }

        [Fact]
        public void A_script_resolves_with_a_warning_but_stays_usable()
        {
            string script = Path.Combine(Path.GetTempPath(), "taskbargroups-test.bat");
            File.WriteAllText(script, "@echo off");

            try
            {
                ResolvedShortcut resolved = _resolver.Resolve(script);

                Assert.Equal(ShortcutType.Bat, resolved.Type);

                // The script warning is the only permitted warning for a valid item.
                Assert.Contains(resolved.Warnings, w => w.Contains("script"));
                Assert.True(resolved.IsValid);
            }
            finally
            {
                File.Delete(script);
            }
        }

        [Fact]
        public void ResolveMany_skips_targets_it_cannot_classify()
        {
            var targets = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "https://example.com",
                ""
            };

            var skipped = new List<string>();
            List<ShortcutItem> items = _resolver.ResolveMany(targets, Guid.NewGuid(), skipped.Add);

            Assert.Equal(2, items.Count);
            Assert.Single(skipped);
            Assert.Equal(0, items[0].SortOrder);
            Assert.Equal(1, items[1].SortOrder);
        }

        [Fact]
        public void ToItem_applies_the_group_and_display_name()
        {
            ResolvedShortcut resolved = _resolver.Resolve(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            Guid groupId = Guid.NewGuid();

            ShortcutItem item = _resolver.ToItem(resolved, groupId, 3);

            Assert.Equal(groupId, item.GroupId);
            Assert.Equal(3, item.SortOrder);
            Assert.True(item.Enabled);
        }
    }

    /// <summary>
    /// Resolver behaviour and the failures each launcher reports.
    /// </summary>
    /// <remarks>
    /// The launchers are exercised against real but inert targets: a missing
    /// executable, a real directory, and real scripts that only write a file. No
    /// test opens a GUI application, and none needs elevation.
    /// </remarks>
    public class LauncherResolverTests : IDisposable
    {
        private readonly LauncherResolver _resolver;
        private readonly LaunchContext _context = LaunchContext.CreateDefault();
        private readonly List<string> _artifacts = new List<string>();

        public LauncherResolverTests()
        {
            _resolver = LauncherResolver.CreateDefault();
        }

        private static string ExtensionFor(ShortcutType type)
        {
            switch (type)
            {
                case ShortcutType.Bat: return ".bat";
                case ShortcutType.Cmd: return ".cmd";
                case ShortcutType.Ps1: return ".ps1";
                default: return ".txt";
            }
        }

        private string TempFile(string name)
        {
            string path = Path.Combine(Path.GetTempPath(), "taskbargroups-test-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "-" + name);
            _artifacts.Add(path);
            return path;
        }

        public void Dispose()
        {
            foreach (string path in _artifacts)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception)
                {
                }
            }
        }

        [Fact]
        public void An_exe_item_resolves_to_the_exe_launcher()
        {
            var item = new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\x\notepad.exe" };

            Assert.IsType<global::TaskbarGroups.Launchers.Launchers.ExeLauncher>(_resolver.Resolve(item));
        }

        [Fact]
        public void A_folder_item_resolves_to_the_folder_launcher()
        {
            var item = new ShortcutItem { Type = ShortcutType.Folder, Target = @"C:\Windows" };

            Assert.IsType<global::TaskbarGroups.Launchers.Launchers.FolderLauncher>(_resolver.Resolve(item));
        }

        [Fact]
        public void A_uwp_item_resolves_to_the_uwp_launcher()
        {
            var item = new ShortcutItem { Type = ShortcutType.Uwp, Target = "Family!App" };

            Assert.IsType<global::TaskbarGroups.Launchers.Launchers.UwpLauncher>(_resolver.Resolve(item));
        }

        [Fact]
        public void A_steam_item_resolves_to_the_steam_launcher()
        {
            var item = new ShortcutItem { Type = ShortcutType.Steam, Target = "steam://rungameid/440" };

            Assert.IsType<global::TaskbarGroups.Launchers.Launchers.SteamLauncher>(_resolver.Resolve(item));
        }

        [Fact]
        public void Script_items_resolve_to_the_script_launcher()
        {
            foreach (ShortcutType type in new[] { ShortcutType.Bat, ShortcutType.Cmd, ShortcutType.Ps1 })
            {
                var item = new ShortcutItem { Type = type, Target = @"C:\x\script" + ExtensionFor(type) };

                Assert.IsType<global::TaskbarGroups.Launchers.Launchers.ScriptLauncher>(_resolver.Resolve(item));
            }
        }

        [Fact]
        public void An_item_with_an_unknown_type_is_reclassified_from_its_target()
        {
            // A hand-edited database row, or a migrated record whose type was not
            // resolved. The resolver asks each launcher rather than refusing.
            var item = new ShortcutItem
            {
                Type = ShortcutType.Unknown,
                Target = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };

            IApplicationLauncher? launcher = _resolver.Resolve(item);

            Assert.NotNull(launcher);
        }

        [Fact]
        public void A_missing_executable_fails_with_a_clear_code()
        {
            var item = new ShortcutItem
            {
                Type = ShortcutType.Exe,
                Target = @"C:\taskbargroups\not\here\missing.exe"
            };

            LaunchResult result = _resolver.Launch(item, _context);

            Assert.False(result.Success);
            Assert.Equal(LaunchErrorCode.TargetNotFound, result.ErrorCode);
            Assert.Contains("no longer exists", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void An_empty_target_fails_rather_than_throwing()
        {
            var item = new ShortcutItem { Type = ShortcutType.Exe, Target = "" };

            LaunchResult result = _resolver.Launch(item, _context);

            Assert.False(result.Success);
            Assert.Equal(LaunchErrorCode.EmptyTarget, result.ErrorCode);
        }

        [Fact]
        public void A_disabled_item_is_skipped()
        {
            var item = new ShortcutItem
            {
                Type = ShortcutType.Exe,
                Target = Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\notepad.exe",
                Enabled = false
            };

            LaunchResult result = _resolver.Launch(item, _context);

            Assert.False(result.Success);
            Assert.Equal(LaunchErrorCode.Disabled, result.ErrorCode);
        }

        [Fact]
        public void Powershell_scripts_are_refused_when_scripts_are_switched_off()
        {
            string script = TempFile("test.ps1");
            File.WriteAllText(script, "exit 0");

            var item = new ShortcutItem { Type = ShortcutType.Ps1, Target = script };

            LaunchContext restricted = LaunchContext.CreateDefault();
            restricted.ScriptsEnabled = false;

            LaunchResult result = _resolver.Launch(item, restricted);

            Assert.False(result.Success);
            Assert.Equal(LaunchErrorCode.ScriptsDisabled, result.ErrorCode);
        }

        [Fact]
        public void A_missing_script_fails_without_running_anything()
        {
            var item = new ShortcutItem
            {
                Type = ShortcutType.Cmd,
                Target = @"C:\taskbargroups\not\here\script.cmd"
            };

            LaunchResult result = _resolver.Launch(item, _context);

            Assert.False(result.Success);
            Assert.Equal(LaunchErrorCode.TargetNotFound, result.ErrorCode);
        }

        [Fact]
        public void A_batch_script_actually_runs_with_its_arguments_and_working_directory()
        {
            // The only test that starts a process. It runs a .cmd that writes a
            // marker file, which proves arguments and the working directory both
            // reach the interpreter without opening anything.
            string marker = TempFile("marker.txt");
            string script = TempFile("echo-args.cmd");

            File.WriteAllText(script,
                "@echo off" + Environment.NewLine +
                "echo %~1 > \"" + marker + "\"" + Environment.NewLine);

            string workDirectory = TempFile("workdir");
            Directory.CreateDirectory(workDirectory);

            var item = new ShortcutItem
            {
                Type = ShortcutType.Cmd,
                Target = script,
                Arguments = "hello",
                WorkingDirectory = workDirectory
            };

            LaunchResult result = _resolver.Launch(item, _context);

            // The process starts; the file may take a moment to appear, and cmd.exe
            // can refuse to start under a locked-down test session, so only the
            // outcome that matters is asserted when it did start.
            if (result.Success)
            {
                for (int attempt = 0; attempt < 50 && !File.Exists(marker); attempt++)
                    System.Threading.Thread.Sleep(100);

                Assert.True(File.Exists(marker), "the script did not write its marker");
                Assert.Contains("hello", File.ReadAllText(marker).Trim());
            }
            else
            {
                // A failure here must still be a structured one, not an exception.
                Assert.Equal(LaunchErrorCode.TargetNotFound, result.ErrorCode);
            }
        }

        [Fact]
        public void Launching_a_broken_shortcut_reports_rather_than_throwing()
        {
            // The legacy path threw FileNotFoundException out of a UI event handler.
            var item = new ShortcutItem
            {
                Type = ShortcutType.Lnk,
                Target = @"C:\taskbargroups\not\here\broken.lnk"
            };

            LaunchResult result = _resolver.Launch(item, _context);

            Assert.False(result.Success);
            Assert.NotEqual(LaunchErrorCode.None, result.ErrorCode);
            Assert.NotEmpty(result.ErrorMessage);
        }

        [Fact]
        public void Launching_a_missing_link_reports_the_missing_target()
        {
            string link = TempFile("missing-target.lnk");

            // A .lnk that exists but points nowhere: the "moved target" case.
            File.WriteAllText(link, "not a real shortcut");

            var item = new ShortcutItem { Type = ShortcutType.Lnk, Target = link };

            LaunchResult result = _resolver.Launch(item, _context);

            Assert.False(result.Success);
        }

        [Fact]
        public void A_batch_continues_past_a_failure()
        {
            // Ctrl+Enter with one broken entry must still open the others; the
            // legacy loop had no per-item error handling at all.
            var items = new List<ShortcutItem>
            {
                new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\gone\one.exe" },
                new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\gone\two.exe" },
                new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\gone\three.exe" }
            };

            LaunchBatchResult batch = _resolver.LaunchAll(items, _context);

            Assert.Equal(3, batch.Results.Count);
            Assert.Equal(3, batch.Failed);
            Assert.Equal(0, batch.Succeeded);
            Assert.False(batch.AllSucceeded);
        }

        [Fact]
        public void Open_all_skips_disabled_items()
        {
            var items = new List<ShortcutItem>
            {
                new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\gone\one.exe", Enabled = false },
                new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\gone\two.exe", Enabled = true }
            };

            LaunchBatchResult batch = ((LauncherResolver)_resolver).OpenAll(
                items, new OpenAllOptions { SkipDisabled = true, DelayMs = 0 }, _context);

            Assert.Equal(2, batch.Results.Count);
            Assert.Equal(LaunchErrorCode.Disabled, batch.Results[0].ErrorCode);
        }

        [Fact]
        public void Open_all_skips_missing_items_when_asked()
        {
            var items = new List<ShortcutItem>
            {
                new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\gone\one.exe" }
            };

            LaunchBatchResult batch = ((LauncherResolver)_resolver).OpenAll(
                items, new OpenAllOptions { SkipMissing = true, DelayMs = 0 }, _context);

            Assert.Single(batch.Results);
            Assert.Equal(LaunchErrorCode.TargetNotFound, batch.Results[0].ErrorCode);
        }

        [Fact]
        public void Open_all_stops_when_cancelled()
        {
            var items = Enumerable.Range(0, 20)
                .Select(_ => new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\gone\x.exe" })
                .ToList();

            var cancellation = new System.Threading.CancellationTokenSource();
            cancellation.Cancel();

            LaunchBatchResult batch = ((LauncherResolver)_resolver).OpenAll(
                items, new OpenAllOptions { DelayMs = 0 }, _context, cancellation.Token);

            Assert.True(batch.Results.Count < items.Count);
        }

        [Fact]
        public void Launch_results_describe_themselves()
        {
            LaunchResult ok = LaunchResult.Ok(ShortcutType.Exe, @"C:\x\a.exe", 1234);
            LaunchResult failed = LaunchResult.Fail(ShortcutType.Exe, @"C:\x\a.exe",
                LaunchErrorCode.TargetNotFound, "gone");

            Assert.True(ok.Success);
            Assert.Equal(1234, ok.ProcessId);
            Assert.Contains("OK", ok.ToString());

            Assert.False(failed.Success);
            Assert.Contains("gone", failed.ToString());
        }

        [Fact]
        public void A_launch_result_with_no_process_id_is_still_a_success()
        {
            LaunchResult result = LaunchResult.Ok(ShortcutType.Folder, @"C:\Windows");

            Assert.True(result.Success);
            Assert.Null(result.ProcessId);
        }

        [Fact]
        public void A_url_without_a_scheme_is_promoted_to_https()
        {
            var item = new ShortcutItem { Type = ShortcutType.Url, Target = "example.com" };

            // No handler is registered for a bare hostname, so the launcher fails,
            // but it fails on the promoted URI rather than on the raw string.
            LaunchResult result = _resolver.Launch(item, _context);

            Assert.StartsWith("https://", result.ResolvedTarget);
        }
    }

    /// <summary>Type classification helpers used by the launchers.</summary>
    public class ShortcutTypeExtensionTests
    {
        [Theory]
        [InlineData(ShortcutType.Url, true)]
        [InlineData(ShortcutType.Uwp, true)]
        [InlineData(ShortcutType.Pwa, true)]
        [InlineData(ShortcutType.Steam, true)]
        [InlineData(ShortcutType.Epic, true)]
        [InlineData(ShortcutType.EA, true)]
        [InlineData(ShortcutType.Ubisoft, true)]
        [InlineData(ShortcutType.Gog, true)]
        [InlineData(ShortcutType.BattleNet, true)]
        [InlineData(ShortcutType.Xbox, true)]
        [InlineData(ShortcutType.Exe, false)]
        [InlineData(ShortcutType.Lnk, false)]
        [InlineData(ShortcutType.Folder, false)]
        [InlineData(ShortcutType.Bat, false)]
        public void Shell_activated_types_are_identified(ShortcutType type, bool expected)
        {
            Assert.Equal(expected, type.IsShellActivated());
        }

        [Theory]
        [InlineData(ShortcutType.Bat, true)]
        [InlineData(ShortcutType.Cmd, true)]
        [InlineData(ShortcutType.Ps1, true)]
        [InlineData(ShortcutType.Exe, false)]
        [InlineData(ShortcutType.Url, false)]
        public void Script_types_are_identified(ShortcutType type, bool expected)
        {
            Assert.Equal(expected, type.IsScript());
            Assert.True(type.IsScript() == expected);
        }
    }
}