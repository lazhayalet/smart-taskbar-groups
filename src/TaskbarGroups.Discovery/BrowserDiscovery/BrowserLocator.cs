using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TaskbarGroups.Core.Interfaces;

namespace TaskbarGroups.Discovery.BrowserDiscovery
{
    /// <summary>
    /// Locates installed Chromium-family browsers and their PWA registrations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Edge, Chrome and Brave all store installed web apps in the same place: a
    /// per-profile <c>Web Applications</c> directory under the user data folder,
    /// with one subdirectory per app holding a manifest and the icons. That layout
    /// is not a published contract, so every read here is defensive: a profile that
    /// has been deleted, a manifest that is not JSON and a directory that has been
    /// removed since the scan started all yield "no PWA here" instead of an
    /// exception.
    /// </para>
    /// <para>
    /// Launching is the documented and supported route rather than the manifest's
    /// own <c>launch_url</c>, because that value points at a page that then has to
    /// ask the site to re-enter app mode. Starting the browser with
    /// <c>--app=</c> and the right profile directory reproduces the installed
    /// behaviour.
    /// </para>
    /// </remarks>
    public sealed class BrowserLocator
    {
        private static readonly (string Name, string RelativeExe)[] KnownBrowsers =
        {
            ("Edge", @"Microsoft\Edge\Application\msedge.exe"),
            ("Chrome", @"Google\Chrome\Application\chrome.exe"),
            ("Brave", @"BraveSoftware\Brave-Browser\Application\brave.exe")
        };

        private readonly IAppLogger? _logger;

        public BrowserLocator(IAppLogger? logger = null)
        {
            _logger = logger;
        }

        /// <summary>Executable paths of the supported browsers that are installed.</summary>
        public IReadOnlyList<string> FindBrowserExecutables()
        {
            var found = new List<string>();

            foreach (var browser in KnownBrowsers)
            {
                string? path = ResolveBrowserPath(browser.RelativeExe);
                if (path != null) found.Add(path);
            }

            return found;
        }

        /// <summary>
        /// Finds the browser that should launch a given PWA target.
        /// </summary>
        /// <remarks>
        /// A target can name its browser explicitly with a
        /// <c>#browser=edge</c> fragment; otherwise the browsers are tried in the
        /// order the user is most likely to have installed them, so the common
        /// single-browser machine behaves predictably.
        /// </remarks>
        public string? FindBrowserFor(string? pwaTarget)
        {
            if (string.IsNullOrWhiteSpace(pwaTarget)) return null;

            string? requested = ExtractFragment(pwaTarget, "browser");
            if (!string.IsNullOrEmpty(requested))
            {
                string? specific = ResolveBrowserPath(FindRelativeExecutable(requested));
                if (specific != null) return specific;
            }

            foreach (var browser in KnownBrowsers)
            {
                string? path = ResolveBrowserPath(browser.RelativeExe);
                if (path != null) return path;
            }

            return null;
        }

        private static string FindRelativeExecutable(string browserName)
        {
            foreach (var browser in KnownBrowsers)
            {
                if (browser.Name.Equals(browserName, StringComparison.OrdinalIgnoreCase))
                    return browser.RelativeExe;
            }
            return KnownBrowsers[0].RelativeExe;
        }

        /// <summary>Icon file for a PWA target, when one can be found.</summary>
        public string? FindIcon(string? pwaTarget)
        {
            if (string.IsNullOrWhiteSpace(pwaTarget)) return null;

            foreach (string profile in EnumerateWebAppRoots())
            {
                foreach (string appDirectory in SafeEnumerateDirectories(profile))
                {
                    string manifestPath = Path.Combine(appDirectory, "manifest.json");
                    if (!File.Exists(manifestPath)) continue;

                    PwaManifest? manifest = ReadManifest(manifestPath);
                    if (manifest == null) continue;

                    if (!MatchesTarget(manifest, pwaTarget)) continue;

                    string? icon = ResolveIcon(appDirectory, manifest.IconPath);
                    if (icon != null) return icon;
                }
            }

            return null;
        }

        /// <summary>Profile directory names for a browser's user data folder.</summary>
        public IReadOnlyList<string> EnumerateProfiles(string? browserName = null)
        {
            var profiles = new List<string>();

            foreach (string userDataRoot in EnumerateUserDataRoots(browserName))
            {
                foreach (string directory in SafeEnumerateDirectories(userDataRoot))
                {
                    string name = Path.GetFileName(directory);
                    if (name.Equals("Crashpad", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("Web Applications", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("System Profile", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.StartsWith("Guest", StringComparison.OrdinalIgnoreCase)) continue;

                    profiles.Add(name);
                }
            }

            return profiles;
        }

        /// <summary>Folders holding installed web apps.</summary>
        public IEnumerable<string> EnumerateWebAppRoots()
        {
            foreach (string userDataRoot in EnumerateUserDataRoots(null))
            {
                string webApps = Path.Combine(userDataRoot, "Web Applications");
                if (Directory.Exists(webApps)) yield return webApps;
            }
        }

        private IEnumerable<string> EnumerateUserDataRoots(string? browserName)
        {
            foreach (var browser in KnownBrowsers)
            {
                if (!string.IsNullOrEmpty(browserName) &&
                    !browser.Name.Equals(browserName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string executable = ResolveBrowserPath(browser.RelativeExe) ?? string.Empty;
                if (executable.Length == 0) continue;

                // Chromium stores user data beside the executable unless overridden.
                string userData = Path.Combine(
                    Path.GetDirectoryName(executable) ?? string.Empty,
                    "User Data");

                if (Directory.Exists(userData)) yield return userData;

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string alternate = Path.Combine(localAppData, browser.Name, "User Data");
                if (Directory.Exists(alternate)) yield return alternate;
            }
        }

        private static string? ResolveBrowserPath(string relativeExe)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string candidate = Path.Combine(localAppData, relativeExe);
            if (File.Exists(candidate)) return candidate;

            // A per-machine install puts the browser under Program Files instead.
            foreach (Environment.SpecialFolder folder in new[]
                     {
                         Environment.SpecialFolder.ProgramFiles,
                         Environment.SpecialFolder.ProgramFilesX86
                     })
            {
                string programFiles = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(programFiles)) continue;

                string perMachine = Path.Combine(programFiles, relativeExe);
                if (File.Exists(perMachine)) return perMachine;
            }

            return null;
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string root)
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

        private static PwaManifest? ReadManifest(string path)
        {
            try
            {
                // Limit the read: these files are small, and an unreadable or huge
                // one must not stall a discovery pass.
                var info = new FileInfo(path);
                if (info.Length > 512 * 1024) return null;

                using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using JsonDocument document = JsonDocument.Parse(stream);

                JsonElement root = document.RootElement;

                var manifest = new PwaManifest
                {
                    Name = ReadString(root, "name") ?? string.Empty,
                    ShortName = ReadString(root, "short_name") ?? string.Empty,
                    StartUrl = ReadString(root, "start_url") ?? string.Empty,
                    Display = ReadString(root, "display") ?? string.Empty
                };

                if (root.TryGetProperty("icons", out JsonElement icons) && icons.ValueKind == JsonValueKind.Array)
                {
                    string? bestPath = null;
                    long bestSize = -1;

                    foreach (JsonElement icon in icons.EnumerateArray())
                    {
                        string? iconPath = ReadString(icon, "src");
                        long sizes = ReadLong(icon, "sizes");

                        // Prefer the largest declared square asset.
                        if (sizes > bestSize)
                        {
                            bestSize = sizes;
                            bestPath = iconPath;
                        }
                    }

                    manifest.IconPath = bestPath ?? string.Empty;
                }

                return manifest;
            }
            catch (JsonException)
            {
                // A manifest the browser wrote but that is not readable JSON. Not
                // offered rather than failing the scan.
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string? ResolveIcon(string appDirectory, string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath)) return null;

            try
            {
                string candidate = Path.Combine(appDirectory, iconPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception)
            {
            }

            return null;
        }

        private static bool MatchesTarget(PwaManifest manifest, string pwaTarget)
        {
            string url = StripFragment(pwaTarget);

            if (string.IsNullOrWhiteSpace(manifest.StartUrl)) return false;

            string start = StripFragment(manifest.StartUrl);

            return string.Equals(start, url, StringComparison.OrdinalIgnoreCase)
                   || url.StartsWith(start, StringComparison.OrdinalIgnoreCase)
                   || start.StartsWith(url, StringComparison.OrdinalIgnoreCase);
        }

        internal static string StripFragment(string value)
        {
            int hash = value.IndexOf('#');
            return hash < 0 ? value : value.Substring(0, hash);
        }

        internal static string? ExtractFragment(string value, string key)
        {
            int hash = value.IndexOf('#');
            if (hash < 0) return null;

            string fragment = value.Substring(hash + 1);
            foreach (string part in fragment.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = part.IndexOf('=');
                if (equals <= 0) continue;

                if (part.Substring(0, equals).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return part.Substring(equals + 1).Trim();
            }

            return null;
        }

        private static string? ReadString(JsonElement element, string property)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(property, out JsonElement value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static long ReadLong(JsonElement element, string property)
        {
            if (element.ValueKind != JsonValueKind.Object) return 0;
            if (!element.TryGetProperty(property, out JsonElement value)) return 0;

            // "sizes" is a string like "512x512".
            if (value.ValueKind == JsonValueKind.String)
            {
                string text = value.GetString() ?? string.Empty;
                int x = text.IndexOf('x');
                if (x > 0 && long.TryParse(text.Substring(0, x), out long width)) return width;
                return 0;
            }

            return value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;
        }

        private sealed class PwaManifest
        {
            public string Name { get; set; } = string.Empty;

            public string ShortName { get; set; } = string.Empty;

            public string StartUrl { get; set; } = string.Empty;

            public string Display { get; set; } = string.Empty;

            public string IconPath { get; set; } = string.Empty;
        }
    }
}