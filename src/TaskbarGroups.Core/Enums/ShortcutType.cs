using System;

namespace TaskbarGroups.Core.Enums
{
    /// <summary>
    /// What kind of thing a shortcut item points at.
    /// </summary>
    /// <remarks>
    /// The legacy model only had a single <c>isWindowsApp</c> flag, which forced
    /// every other kind of target to be treated as a physical executable. Every
    /// launcher in this application branches on this enum instead, so a folder,
    /// a URL, a Store app and a PWA each get the code path they actually need.
    /// </remarks>
    public enum ShortcutType
    {
        /// <summary>Type could not be determined.</summary>
        Unknown = 0,

        /// <summary>A native executable that can be created directly.</summary>
        Exe = 1,

        /// <summary>A Windows shell link (.lnk). Its real target is resolved first.</summary>
        Lnk = 2,

        /// <summary>A directory, opened in Explorer.</summary>
        Folder = 3,

        /// <summary>A URL or any other shell-activated URI, including protocol handlers.</summary>
        Url = 4,

        /// <summary>A packaged app, launched through the shell:appsFolder namespace.</summary>
        Uwp = 5,

        /// <summary>An installed browser progressive web app.</summary>
        Pwa = 6,

        /// <summary>A Steam game, launched through the steam:// protocol.</summary>
        Steam = 7,

        /// <summary>A Windows batch file.</summary>
        Bat = 8,

        /// <summary>A Windows command script.</summary>
        Cmd = 9,

        /// <summary>A PowerShell script.</summary>
        Ps1 = 10,

        /// <summary>An application reference (.appref-ms).</summary>
        AppRefMs = 11,

        /// <summary>A packaged MSIX application.</summary>
        Msix = 12,

        /// <summary>An Epic Games Store title.</summary>
        Epic = 13,

        /// <summary>An EA application or game.</summary>
        EA = 14,

        /// <summary>A Ubisoft Connect title.</summary>
        Ubisoft = 15,

        /// <summary>A GOG Galaxy title.</summary>
        Gog = 16,

        /// <summary>A Battle.net application or game.</summary>
        BattleNet = 17,

        /// <summary>An Xbox or Game Pass title.</summary>
        Xbox = 18
    }

    /// <summary>Helpers that keep <see cref="ShortcutType"/> knowledge in one place.</summary>
    public static class ShortcutTypeExtensions
    {
        /// <summary>
        /// True for the types that are dispatched to a Windows shell protocol
        /// handler rather than created as a process.
        /// </summary>
        public static bool IsShellActivated(this ShortcutType type)
        {
            switch (type)
            {
                case ShortcutType.Url:
                case ShortcutType.Uwp:
                case ShortcutType.Pwa:
                case ShortcutType.Steam:
                case ShortcutType.Epic:
                case ShortcutType.EA:
                case ShortcutType.Ubisoft:
                case ShortcutType.Gog:
                case ShortcutType.BattleNet:
                case ShortcutType.Xbox:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True for script types, which the editor warns about before saving.</summary>
        public static bool IsScript(this ShortcutType type)
        {
            return type == ShortcutType.Bat || type == ShortcutType.Cmd || type == ShortcutType.Ps1;
        }

        /// <summary>True when the target is a script or an item that runs code on click.</summary>
        public static bool ExecutesCode(this ShortcutType type)
        {
            return IsScript(type);
        }
    }
}