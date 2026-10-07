using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TaskbarGroups.Core.Interfaces;

namespace TaskbarGroups.Discovery.SteamDiscovery
{
    /// <summary>
    /// Reads the installed Steam library and its games.
    /// </summary>
    /// <remarks>
    /// Steam's registry value for a library folder is consulted first because it is
    /// the authoritative list, then <c>steamapps/libraryfolders.vdf</c> for
    /// libraries installed after the registry key was last written, then the default
    /// location. A game is a folder under <c>steamapps/common</c> containing
    /// <c>appmanifest_&lt;id&gt;.acf</c>, which is the file that holds the
    /// authoritative app id and display name - not the folder name, which is a
    /// developer-chosen label and frequently differs.
    /// </remarks>
    public sealed class SteamLibraryReader
    {
        private readonly IAppLogger? _logger;

        public SteamLibraryReader(IAppLogger? logger = null)
        {
            _logger = logger;
        }

        /// <summary>Where Steam is installed, or null.</summary>
        public string? FindSteamRoot()
        {
            foreach (string candidate in SteamRootCandidates())
            {
                if (IsSteamRoot(candidate)) return candidate;
            }
            return null;
        }

        private static IEnumerable<string> SteamRootCandidates()
        {
            var roots = new List<string>();

            try
            {
                using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                string? path = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrWhiteSpace(path)) roots.Add(path!);

                using Microsoft.Win32.RegistryKey? machine = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                string? machinePath = machine?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(machinePath)) roots.Add(machinePath!);
            }
            catch (Exception)
            {
                // A locked-down registry: fall through to the default locations.
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFiles)) roots.Add(Path.Combine(programFiles, "Steam"));

            string programFiles64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles64)) roots.Add(Path.Combine(programFiles64, "Steam"));

            return roots;
        }

        private static bool IsSteamRoot(string path)
        {
            try
            {
                return Directory.Exists(Path.Combine(path, "steamapps"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Every library folder on this machine.</summary>
        public IReadOnlyList<string> EnumerateLibraries()
        {
            var libraries = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string? root = FindSteamRoot();
            if (root != null && seen.Add(root)) libraries.Add(root);

            foreach (string libraryRoot in EnumerateVdfLibraries(root))
            {
                if (seen.Add(libraryRoot)) libraries.Add(libraryRoot);
            }

            return libraries;
        }

        private IEnumerable<string> EnumerateVdfLibraries(string? steamRoot)
        {
            var results = new List<string>();
            if (steamRoot != null)
            {
                try
                {
                    string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");

                    if (File.Exists(vdf))
                    {
                        // The VDF is a Valve KeyValues file, not JSON, but the
                        // library block is a list of quoted absolute paths, so a
                        // targeted scan avoids writing a full parser.
                        string text = File.ReadAllText(vdf);
                        int index = 0;

                        while (true)
                        {
                            int start = text.IndexOf("\"path\"", index, StringComparison.OrdinalIgnoreCase);
                            if (start < 0) break;

                            int colon = text.IndexOf(':', start);
                            if (colon < 0) break;

                            int open = text.IndexOf('"', colon + 1);
                            if (open < 0) break;

                            int close = text.IndexOf('"', open + 1);
                            if (close < 0) break;

                            string path = text.Substring(open + 1, close - open - 1);
                            if (path.Length > 0 && Directory.Exists(path)) results.Add(path);

                            index = close + 1;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Log(SystemLogLevel.Debug, "Discovery", "Steam library list unreadable", ex);
                }
            }

            return results;
        }

        /// <summary>Installed games, one record per app manifest.</summary>
        public IReadOnlyList<SteamGame> EnumerateGames()
        {
            var games = new List<SteamGame>();

            foreach (string library in EnumerateLibraries())
            {
                string common = Path.Combine(library, "steamapps", "common");
                if (!Directory.Exists(common)) continue;

                string[] manifestFiles;
                try
                {
                    manifestFiles = Directory.GetFiles(common, "appmanifest_*.acf", SearchOption.AllDirectories);
                }
                catch (Exception ex)
                {
                    _logger?.Log(SystemLogLevel.Debug, "Discovery", "Steam library unreadable: " + library, ex);
                    continue;
                }

                foreach (string manifestFile in manifestFiles)
                {
                    SteamGame? game = ReadManifest(manifestFile, library);
                    if (game != null) games.Add(game);
                }
            }

            return games;
        }

        private SteamGame? ReadManifest(string manifestFile, string libraryRoot)
        {
            try
            {
                Dictionary<string, string> values = ParseKeyValues(File.ReadAllText(manifestFile));

                values.TryGetValue("appid", out string? appId);
                values.TryGetValue("name", out string? name);

                if (string.IsNullOrWhiteSpace(appId)) return null;

                return new SteamGame
                {
                    AppId = appId!,
                    Name = string.IsNullOrWhiteSpace(name) ? appId! : name!,
                    InstallPath = Path.GetDirectoryName(manifestFile) ?? libraryRoot,
                    LibraryRoot = libraryRoot,
                    Uri = "steam://rungameid/" + appId
                };
            }
            catch (Exception ex)
            {
                _logger?.Log(SystemLogLevel.Debug, "Discovery", "Steam manifest unreadable: " + manifestFile, ex);
                return null;
            }
        }

        /// <summary>
        /// Minimal Valve KeyValues reader. Handles quoted values, which is all the
        /// fields this needs, and ignores everything else.
        /// </summary>
        internal static Dictionary<string, string> ParseKeyValues(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int index = 0;
            while (index < text.Length)
            {
                int keyStart = text.IndexOf('"', index);
                if (keyStart < 0) break;

                int keyEnd = text.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;

                string key = text.Substring(keyStart + 1, keyEnd - keyStart - 1);
                index = keyEnd + 1;

                int valueStart = text.IndexOf('"', index);
                if (valueStart < 0) break;

                int valueEnd = text.IndexOf('"', valueStart + 1);
                if (valueEnd < 0) break;

                values[key] = text.Substring(valueStart + 1, valueEnd - valueStart - 1);
                index = valueEnd + 1;
            }

            return values;
        }

        /// <summary>Artwork file for a game, when the client has cached it.</summary>
        public string? FindArtwork(string? steamRoot, string? steamUri)
        {
            if (string.IsNullOrWhiteSpace(steamRoot) || string.IsNullOrWhiteSpace(steamUri)) return null;

            string? appId = ExtractAppId(steamUri);
            if (appId == null) return null;

            string cache = Path.Combine(steamRoot, "appcache", "librarycache");
            if (!Directory.Exists(cache)) return null;

            try
            {
                string[] matches = Directory.GetFiles(cache, "*_" + appId + "_*.ico", SearchOption.AllDirectories);
                if (matches.Length == 0) matches = Directory.GetFiles(cache, "*_" + appId + "_*.*", SearchOption.AllDirectories);
                if (matches.Length == 0) return null;

                foreach (string match in matches)
                {
                    if (match.IndexOf("_grid_", StringComparison.OrdinalIgnoreCase) >= 0) return match;
                }

                return matches[0];
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static string? ExtractAppId(string steamUri)
        {
            const string marker = "rungameid/";
            int index = steamUri.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;

            string tail = steamUri.Substring(index + marker.Length);
            int end = tail.IndexOfAny(new[] { '/', '?', '&', ' ' });
            if (end >= 0) tail = tail.Substring(0, end);

            return tail.Trim().Length > 0 ? tail.Trim() : null;
        }
    }

    /// <summary>One installed Steam title.</summary>
    public sealed class SteamGame
    {
        public string AppId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string InstallPath { get; set; } = string.Empty;

        public string LibraryRoot { get; set; } = string.Empty;

        /// <summary>Protocol URI that launches the game.</summary>
        public string Uri { get; set; } = string.Empty;
    }
}