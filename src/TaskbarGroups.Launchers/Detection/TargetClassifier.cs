using System;
using System.Collections.Generic;
using System.IO;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Windows.Shell;

namespace TaskbarGroups.Launchers.Detection
{
    /// <summary>
    /// Decides what a dropped or picked target actually is.
    /// </summary>
    /// <remarks>
    /// The legacy editor had no type detection at all: <c>frmGroup.addShortcut</c>
    /// stored whatever path it was handed in a <c>ProgramShortcut</c> and
    /// <c>frmMain.OpenFile</c> handed that straight to <c>Process.Start</c>. A
    /// dropped folder therefore produced a shortcut that could never launch, and a
    /// dropped <c>.url</c> or <c>steam://</c> link produced one that threw at
    /// launch time.
    ///
    /// Classification order matters and is deliberate:
    /// <list type="number">
    /// <item>Is it a URI that is not a file path? Then it is shell-activated.</item>
    /// <item>Does it exist as a directory? Folder.</item>
    /// <item>Is it an AppUserModelID? Packaged app.</item>
    /// <item>Otherwise classify by extension.</item>
    /// </list>
    /// </remarks>
    public static class TargetClassifier
    {
        /// <summary>Extensions that map to script launchers.</summary>
        private static readonly Dictionary<string, ShortcutType> ByExtension =
            new Dictionary<string, ShortcutType>(StringComparer.OrdinalIgnoreCase)
            {
                { ".exe", ShortcutType.Exe },
                { ".com", ShortcutType.Exe },
                { ".lnk", ShortcutType.Lnk },
                { ".url", ShortcutType.Url },
                { ".bat", ShortcutType.Bat },
                { ".cmd", ShortcutType.Cmd },
                { ".ps1", ShortcutType.Ps1 },
                { ".psm1", ShortcutType.Ps1 },
                { ".appref-ms", ShortcutType.AppRefMs }
            };

        /// <summary>
        /// URI schemes that mean "launch this with a registered protocol handler",
        /// mapped to the type the UI will show.
        /// </summary>
        private static readonly Dictionary<string, ShortcutType> ByScheme =
            new Dictionary<string, ShortcutType>(StringComparer.OrdinalIgnoreCase)
            {
                { "steam", ShortcutType.Steam },
                { "epic", ShortcutType.Epic },
                { "origin", ShortcutType.EA },
                { "uplay", ShortcutType.Ubisoft },
                { "ubisoft", ShortcutType.Ubisoft },
                { "goggalaxy", ShortcutType.Gog },
                { "battle", ShortcutType.BattleNet },
                { "shell", ShortcutType.Uwp },
                { "ms-msdt", ShortcutType.Url },
                { "mailto", ShortcutType.Url },
                { "http", ShortcutType.Url },
                { "https", ShortcutType.Url },
                { "file", ShortcutType.Folder }
            };

        /// <summary>Classifies a raw target. Never throws.</summary>
        public static ShortcutType Classify(string? rawTarget)
        {
            if (string.IsNullOrWhiteSpace(rawTarget)) return ShortcutType.Unknown;

            string target = rawTarget.Trim();

            // 1. shell:appsFolder\<AppUserModelID>
            if (target.StartsWith("shell:appsFolder", StringComparison.OrdinalIgnoreCase))
                return ShortcutType.Uwp;

            // 2. An absolute URI whose scheme we recognise. A Windows path also
            //    parses as a URI with the "file" scheme, so the path forms are
            //    checked first.
            if (!LooksLikeWindowsPath(target))
            {
                if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
                {
                    if (ByScheme.TryGetValue(uri.Scheme, out ShortcutType byScheme))
                        return byScheme;

                    // Any other registered protocol is still shell-activated and
                    // better represented as a URL than as an unknown exe.
                    if (uri.Scheme.Length > 1)
                        return ShortcutType.Url;
                }
            }

            // 3. A bare AppUserModelID, as stored by a Store shortcut.
            if (LooksLikeAppUserModelId(target)) return ShortcutType.Uwp;

            string expanded = StringHelpers.ExpandPath(target);

            // 4. An existing directory is a folder, whatever it is named. This must
            //    come before the extension check: a directory called "tools.exe"
            //    is a directory.
            if (Directory.Exists(expanded)) return ShortcutType.Folder;

            // 5. Extension mapping.
            string extension = Path.GetExtension(expanded);
            if (extension.Length > 0 && ByExtension.TryGetValue(extension, out ShortcutType mapped))
                return mapped;

            // 6. A .lnk to something. Resolve it: a link to a folder is a folder
            //    shortcut and a link to a URI is shell-activated, which is what
            //    makes Steam and Office shortcuts work.
            if (string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                ShortcutType? resolved = ResolveLinkType(expanded);
                if (resolved.HasValue) return resolved.Value;
                return ShortcutType.Lnk;
            }

            if (extension.Length == 0 && File.Exists(expanded)) return ShortcutType.Exe;

            return ShortcutType.Unknown;
        }

        /// <summary>
        /// Classifies a link by its target. Returns null when the link cannot be
        /// read, so the caller can still treat it as a plain .lnk rather than
        /// silently dropping it.
        /// </summary>
        public static ShortcutType? ResolveLinkType(string linkPath)
        {
            if (string.IsNullOrWhiteSpace(linkPath)) return null;
            if (!File.Exists(linkPath)) return null;

            try
            {
                ShellLinkInfo info = ShellLinkReader.ReadAll(linkPath);
                if (!info.IsValid) return null;

                // A link whose arguments reference an AppUserModelID is how
                // Store shortcuts are recorded; the target is the shell itself.
                if (!string.IsNullOrWhiteSpace(info.TargetPath))
                {
                    if (info.TargetIsUri)
                    {
                        if (info.TargetPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                            return ShortcutType.Uwp;

                        return Classify(info.TargetPath);
                    }

                    if (Directory.Exists(info.TargetPath)) return ShortcutType.Folder;
                    if (File.Exists(info.TargetPath)) return Classify(info.TargetPath);
                }

                if (LooksLikeAppUserModelId(info.Arguments)) return ShortcutType.Uwp;

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>True for <c>PackageFamilyName!AppName</c> shaped identifiers.</summary>
        public static bool LooksLikeAppUserModelId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            string trimmed = value.Trim();
            int bang = trimmed.IndexOf('!');
            if (bang <= 0 || bang == trimmed.Length - 1) return false;

            string family = trimmed.Substring(0, bang);
            string application = trimmed.Substring(bang + 1);

            if (family.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;

            // A package family name is a dotted identifier; an application id is a
            // dotted identifier too. Neither may look like a path, because a path
            // with a bang in it is a file, not a package reference.
            if (trimmed.IndexOf('\\') >= 0 || trimmed.IndexOf('/') >= 0) return false;
            if (trimmed.IndexOf(':') >= 0) return false;

            if (family.Length < 3 || family.Length > 256) return false;
            if (application.Length < 1 || application.Length > 64) return false;

            return true;
        }

        private static bool LooksLikeWindowsPath(string value)
        {
            if (value.Length >= 2 && value[1] == ':') return true;
            if (value.StartsWith(@"\\", StringComparison.Ordinal)) return true;
            if (value.StartsWith(@"//", StringComparison.Ordinal)) return true;
            return false;
        }
    }
}