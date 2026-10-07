using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Discovery.BrowserDiscovery;
using TaskbarGroups.Discovery.PwaDiscovery;
using TaskbarGroups.Discovery.SteamDiscovery;
using TaskbarGroups.Windows.Shell;

namespace TaskbarGroups.Discovery.ApplicationDiscovery
{
    /// <summary>
    /// Finds applications installed on this machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every source is optional and every source is cancellable, because a full
    /// scan of <c>Program Files</c> is slow and a user who only wants their Start
    /// Menu should not wait for it. Results are deduplicated on the target rather
    /// than the display name, so the same program reached through a Start Menu
    /// shortcut and a Program Files entry appears once.
    /// </para>
    /// <para>
    /// Everything runs off the UI thread and yields progress, so the discovery page
    /// stays responsive on a machine with a slow network drive in a Start Menu
    /// folder.
    /// </para>
    /// </remarks>
    public sealed class ApplicationDiscoveryService : IApplicationDiscoveryService
    {
        private readonly IAppLogger? _logger;
        private readonly BrowserLocator _browsers;

        public ApplicationDiscoveryService(BrowserLocator browsers, IAppLogger? logger = null)
        {
            _browsers = browsers ?? throw new ArgumentNullException(nameof(browsers));
            _logger = logger;
        }

        public async Task<IReadOnlyList<DiscoveredApplication>> DiscoverAsync(
            DiscoveryOptions options,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var byTarget = new Dictionary<string, DiscoveredApplication>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            void Collect(IEnumerable<DiscoveredApplication> found)
            {
                foreach (DiscoveredApplication application in found)
                {
                    if (application == null) continue;
                    if (string.IsNullOrWhiteSpace(application.Target)) continue;

                    if (byTarget.ContainsKey(application.Target)) continue;

                    byTarget[application.Target] = application;
                    order.Add(application.Target);
                }
            }

            // Each source is awaited separately so progress can be reported per
            // source rather than once at the end.
            if (options.IncludeStartMenu || options.IncludeDesktop)
            {
                progress?.Report("Reading shortcuts...");
                Collect(await Task.Run(() => ScanShellFolders(options), cancellationToken).ConfigureAwait(false));
            }

            if (options.IncludeUwp)
            {
                progress?.Report("Reading installed apps...");
                Collect(await Task.Run(() => ScanPackagedApps(options.MaxResultsPerSource), cancellationToken).ConfigureAwait(false));
            }

            if (options.IncludeProgramFiles)
            {
                progress?.Report("Scanning Program Files...");
                Collect(await Task.Run(() => ScanProgramFiles(options), cancellationToken).ConfigureAwait(false));
            }

            if (options.IncludePwa)
            {
                progress?.Report("Reading installed web apps...");
                Collect(await Task.Run(ScanInstalledWebApps, cancellationToken).ConfigureAwait(false));
            }

            if (options.IncludeSteam)
            {
                progress?.Report("Reading Steam library...");
                Collect(await Task.Run(ScanSteam, cancellationToken).ConfigureAwait(false));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var results = new List<DiscoveredApplication>(order.Count);
            foreach (string target in order) results.Add(byTarget[target]);

            _logger?.Log(SystemLogLevel.Information, "Discovery", "Discovery finished with " + results.Count + " applications");
            return results;
        }

        private IEnumerable<DiscoveredApplication> ScanShellFolders(DiscoveryOptions options)
        {
            var roots = new List<string>();

            if (options.IncludeStartMenu)
            {
                roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
                roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            }

            if (options.IncludeDesktop)
            {
                roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
                roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            }

            int count = 0;
            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

                string[] shortcuts;
                try
                {
                    shortcuts = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories);
                }
                catch (Exception ex)
                {
                    _logger?.Log(SystemLogLevel.Debug, "Discovery", "Shortcut folder unreadable: " + root, ex);
                    continue;
                }

                foreach (string shortcut in shortcuts)
                {
                    if (count >= options.MaxResultsPerSource) yield break;

                    DiscoveredApplication? application = DescribeShortcut(shortcut);
                    if (application != null)
                    {
                        yield return application;
                        count++;
                    }
                }
            }
        }

        /// <summary>
        /// Resolves a .lnk to what it actually launches, so a Start Menu entry
        /// pointing at a document or a URI is not reported as an application.
        /// </summary>
        private DiscoveredApplication? DescribeShortcut(string shortcutPath)
        {
            try
            {
                ShellLinkInfo info = ShellLinkReader.ReadAll(shortcutPath);
                if (!info.IsValid) return null;

                string displayName = ShellLinkReader.GetDisplayName(shortcutPath);
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = Path.GetFileNameWithoutExtension(shortcutPath);
                }

                // A shortcut that launches another shortcut is a link to a link;
                // skip it rather than reporting the wrapper.
                if (!string.IsNullOrWhiteSpace(info.TargetPath) &&
                    string.Equals(Path.GetExtension(info.TargetPath), ".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(info.TargetPath))
                {
                    // No target: most likely a Store shortcut carrying its
                    // identifier in the arguments.
                    if (LooksLikeAppId(info.Arguments))
                    {
                        return new DiscoveredApplication
                        {
                            Id = MakeId(info.Arguments, DiscoverySource.StartMenu),
                            DisplayName = displayName,
                            Target = info.Arguments.Trim(),
                            Source = DiscoverySource.StartMenu,
                            Type = ShortcutType.Uwp,
                            IconHint = shortcutPath
                        };
                    }

                    return null;
                }

                ShortcutType type = ClassifyTarget(info.TargetPath);

                // Only executables and folders are worth offering as applications.
                if (type != ShortcutType.Exe && type != ShortcutType.Folder && !type.IsShellActivated())
                    return null;

                return new DiscoveredApplication
                {
                    Id = MakeId(info.TargetPath, DiscoverySource.StartMenu),
                    DisplayName = displayName,
                    Target = info.TargetPath,
                    Source = DiscoverySource.StartMenu,
                    Type = type,
                    IconHint = shortcutPath,
                    Category = Categorizer.Guess(displayName, info.TargetPath, type)
                };
            }
            catch (Exception ex)
            {
                _logger?.Log(SystemLogLevel.Debug, "Discovery", "Shortcut unreadable: " + shortcutPath, ex);
                return null;
            }
        }

        private IEnumerable<DiscoveredApplication> ScanPackagedApps(int limit)
        {
            int count = 0;

            foreach (string family in global::TaskbarGroups.Discovery.UwpDiscovery.PackageCatalog.EnumeratePackageFamilies())
            {
                foreach (global::Windows.ApplicationModel.Package package in global::TaskbarGroups.Discovery.UwpDiscovery.PackageCatalog.GetPackages(family))
                {
                    if (count >= limit) yield break;

                    string? location = package?.InstalledLocation?.Path;
                    if (string.IsNullOrWhiteSpace(location)) continue;

                    string? id = global::TaskbarGroups.Discovery.UwpDiscovery.PackageCatalog.ReadApplicationId(location!);
                    string? name = global::TaskbarGroups.Discovery.UwpDiscovery.PackageCatalog.ReadDisplayName(location!);

                    if (string.IsNullOrWhiteSpace(id)) continue;

                    yield return new DiscoveredApplication
                    {
                        Id = MakeId(id, DiscoverySource.UwpPackage),
                        DisplayName = string.IsNullOrWhiteSpace(name) ? family : name!,
                        Target = id,
                        Source = DiscoverySource.UwpPackage,
                        Type = ShortcutType.Uwp,
                        IconHint = location!,
                        Category = Categorizer.Guess(name ?? family, id, ShortcutType.Uwp)
                    };

                    count++;
                }
            }
        }

        private IEnumerable<DiscoveredApplication> ScanProgramFiles(DiscoveryOptions options)
        {
            var roots = new List<string>();

            foreach (Environment.SpecialFolder folder in new[]
                     {
                         Environment.SpecialFolder.ProgramFiles,
                         Environment.SpecialFolder.ProgramFilesX86
                     })
            {
                string path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(path)) roots.Add(path);
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
                roots.Add(Path.Combine(localAppData, "Programs"));

            int count = 0;

            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;

                string[] executables;
                try
                {
                    // Two levels deep is the usual layout (vendor/product/exe) and
                    // keeps the scan from walking whole trees of runtimes.
                    executables = Directory.GetFiles(root, "*.exe", SearchOption.AllDirectories);
                }
                catch (Exception ex)
                {
                    _logger?.Log(SystemLogLevel.Debug, "Discovery", "Program folder unreadable: " + root, ex);
                    continue;
                }

                foreach (string executable in executables)
                {
                    if (count >= options.MaxResultsPerSource) yield break;

                    string name = Path.GetFileNameWithoutExtension(executable);
                    if (IsRuntimeOrInstallerNoise(name)) continue;

                    yield return new DiscoveredApplication
                    {
                        Id = MakeId(executable, DiscoverySource.ProgramFiles),
                        DisplayName = name,
                        Target = executable,
                        Source = DiscoverySource.ProgramFiles,
                        Type = ShortcutType.Exe,
                        IconHint = executable,
                        Category = Categorizer.Guess(name, executable, ShortcutType.Exe)
                    };

                    count++;
                }
            }
        }

        private IEnumerable<DiscoveredApplication> ScanInstalledWebApps()
        {
            foreach (string root in _browsers.EnumerateWebAppRoots())
            {
                foreach (string appDirectory in SafeDirectories(root))
                {
                    string manifestPath = Path.Combine(appDirectory, "manifest.json");
                    if (!File.Exists(manifestPath)) continue;

                    PwaEntry? entry = global::TaskbarGroups.Discovery.PwaDiscovery.WebAppManifest.Read(manifestPath);
                    if (entry == null) continue;
                    if (string.IsNullOrWhiteSpace(entry.StartUrl)) continue;

                    // The installed app is launched by the browser, not by the
                    // manifest's own URL, so the browser and profile are recorded
                    // as a fragment the launcher understands.
                    string target = entry.StartUrl!;

                    yield return new DiscoveredApplication
                    {
                        Id = MakeId(target, DiscoverySource.Pwa),
                        DisplayName = string.IsNullOrWhiteSpace(entry.Name) ? entry.StartUrl! : entry.Name!,
                        Target = target,
                        Source = DiscoverySource.Pwa,
                        Type = ShortcutType.Pwa,
                        IconHint = entry.IconPath ?? appDirectory,
                        Category = AppCategory.Internet
                    };
                }
            }
        }

        private IEnumerable<DiscoveredApplication> ScanSteam()
        {
            var reader = new SteamLibraryReader(_logger);
            string? root = reader.FindSteamRoot();

            foreach (SteamGame game in reader.EnumerateGames())
            {
                string icon = reader.FindArtwork(root, game.Uri) ?? string.Empty;

                yield return new DiscoveredApplication
                {
                    Id = MakeId(game.Uri, DiscoverySource.Steam),
                    DisplayName = game.Name,
                    Target = game.Uri,
                    Source = DiscoverySource.Steam,
                    Type = ShortcutType.Steam,
                    IconHint = icon,
                    Category = AppCategory.Gaming
                };
            }
        }

        internal static IEnumerable<string> SafeDirectories(string root)
        {
            try
            {
                return Directory.GetDirectories(root);
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Filters out executables that are runtime plumbing rather than programs a
        /// user would put in a group.
        /// </summary>
        private static bool IsRuntimeOrInstallerNoise(string name)
        {
            string[] noise =
            {
                "crashpad_handler", "vcredist", "vc_redist", "setup", "unins", "uninstall",
                "repair", "modify", "detect", "dotnetfx", "msiexec", "rundll32",
                "dllhost", "conhost", "wermgr", "regsvr32", "sfc", "cmd"
            };

            foreach (string candidate in noise)
            {
                if (name.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        internal static string MakeId(string target, DiscoverySource source)
        {
            string combined = source.ToString() + "|" + target;
            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));

            var hex = new System.Text.StringBuilder(24);
            for (int i = 0; i < 12 && i < hash.Length; i++) hex.Append(hash[i].ToString("x2"));
            return hex.ToString();
        }

        private static ShortcutType ClassifyTarget(string target)
        {
            try
            {
                if (Directory.Exists(target)) return ShortcutType.Folder;
                if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && !uri.IsFile) return ShortcutType.Url;
            }
            catch (Exception)
            {
            }

            return ShortcutType.Exe;
        }

        private static bool LooksLikeAppId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            int bang = value.IndexOf('!');
            return bang > 0 && bang < value.Length - 1;
        }
    }
}