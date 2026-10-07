using System;
using System.Collections.Generic;
using System.IO;
using TaskbarGroups.Core.Enums;

namespace TaskbarGroups.Discovery.ApplicationDiscovery
{
    /// <summary>
    /// Assigns a coarse category from a name, a path and a type.
    /// </summary>
    /// <remarks>
    /// Deliberately rule-based and local. The brief allows an optional AI
    /// categorisation pass, but requiring a network call or an API key for
    /// something a keyword list does adequately would make the core feature
    /// network-dependent for no benefit. These rules are the offline default; a
    /// future assisted pass could override them, never replace them.
    /// </remarks>
    public static class Categorizer
    {
        private static readonly (AppCategory Category, string[] Keywords)[] Rules =
        {
            (AppCategory.Development, new[]
            {
                "visual studio", "devenv", "vscode", "code.exe", "git", "gitkraken", "github desktop",
                "jetbrains", "intellij", "pycharm", "webstorm", "android studio", "eclipse", "netbeans",
                "xcode", "sublime", "notepad++", "atom", "unity", "unreal", "godot", "source insight",
                "wireshark", "postman", "docker", "kubernetes", "terraform", "ansible", "cygwin", "mingw",
                "msys", "putty", "winscp", "dev-c++", "codeblocks", "rider", "resharper", "nuget"
            }),
            (AppCategory.Gaming, new[]
            {
                "steam", "epic", "gog", "battle.net", "blizzard", "riot", "ubisoft", "uplay",
                "origin", "ea app", "pokemon", "minecraft", "league", "valorant", "counter-strike",
                "overwatch", "diablo", "wow", "warcraft", "dota", "apex", "destiny", "rust", "garry"
            }),
            (AppCategory.Office, new[]
            {
                "word", "excel", "powerpoint", "outlook", "onenote", "visio", "publisher",
                "microsoft 365", "office", "libreoffice", "openoffice", "acrobat", "adobe",
                "onenote", "thunderbird", "calibre", "evernote", "notion", "obsidian", "zotero"
            }),
            (AppCategory.Internet, new[]
            {
                "chrome", "firefox", "edge", "msedge", "brave", "opera", "vivaldi", "safari",
                "browser", "tor", "waterfox", "netscape"
            }),
            (AppCategory.Media, new[]
            {
                "vlc", "mpv", "potplayer", "kodi", "plex", "spotify", "itunes", "foobar",
                "audacity", "obs", "premiere", "davinci", "handbrake", "ffmpeg", "photos", "picasa",
                "gimp", "krita", "paint", "snipping"
            }),
            (AppCategory.Graphics, new[]
            {
                "photoshop", "illustrator", "inkscape", "blender", "maya", "cinema 4d", "houdini",
                "affinity", "designer", "paint.net", "krita", "darktable"
            }),
            (AppCategory.Communication, new[]
            {
                "discord", "slack", "teams", "zoom", "skype", "telegram", "signal", "whatsapp",
                "element", "matrix", "twitch", "viber", "wire", "messenger"
            }),
            (AppCategory.Education, new[]
            {
                "duolingo", "moodle", "anki", "quizlet", "khan", "mathematica", "maple",
                "classroom", "coursera", "udemy", "zotero"
            }),
            (AppCategory.System, new[]
            {
                "task manager", "control panel", "regedit", "services", "msconfig", "device manager",
                "disk cleanup", "defragment", "system", "registry", "event viewer", "cmd", "powershell",
                "terminal", "hyper-v", "vmware", "virtualbox", "nvidia", "intel", "amd", "realtek"
            }),
            (AppCategory.Utilities, new[]
            {
                "7-zip", "winrar", "winzip", "7zip", "notepad", "calculator", "everything",
                "ccleaner", "revouninstall", "treesize", "powertoys", "sharex", "snipaste",
                "ditto", "autohotkey", "fzf", "jq", "ripgrep"
            })
        };

        /// <summary>Best-effort category for a discovered application.</summary>
        public static AppCategory Guess(string? displayName, string? target, ShortcutType type)
        {
            // Some types are categorically fixed regardless of the name.
            if (type == ShortcutType.Steam || type == ShortcutType.Epic || type == ShortcutType.EA ||
                type == ShortcutType.Gog || type == ShortcutType.BattleNet || type == ShortcutType.Ubisoft ||
                type == ShortcutType.Xbox)
            {
                return AppCategory.Gaming;
            }

            if (type == ShortcutType.Url || type == ShortcutType.Pwa) return AppCategory.Internet;

            string haystack = ((displayName ?? string.Empty) + " " + FileNameOf(target)).ToLowerInvariant();
            if (haystack.Trim().Length == 0) return AppCategory.Other;

            AppCategory best = AppCategory.Other;
            int bestScore = 0;

            foreach (var rule in Rules)
            {
                foreach (string keyword in rule.Keywords)
                {
                    if (!haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;

                    // Longer keywords are more specific, so "visual studio" beats a
                    // bare "studio" that another rule might also match.
                    int score = keyword.Length;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = rule.Category;
                    }
                }
            }

            return bestScore > 0 ? best : AppCategory.Other;
        }

        private static string FileNameOf(string? target)
        {
            if (string.IsNullOrWhiteSpace(target)) return string.Empty;

            try
            {
                string name = Path.GetFileNameWithoutExtension(target);
                return string.IsNullOrEmpty(name) ? string.Empty : name;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>All categories, for the discovery filter UI.</summary>
        public static IReadOnlyList<AppCategory> All => new[]
        {
            AppCategory.Development, AppCategory.Gaming, AppCategory.Office, AppCategory.Internet,
            AppCategory.Media, AppCategory.Graphics, AppCategory.Communication, AppCategory.System,
            AppCategory.Utilities, AppCategory.Education, AppCategory.Other
        };
    }
}