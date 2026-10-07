using System;

namespace TaskbarGroups.Core.Enums
{
    /// <summary>Background/appearance preset applied to a group popup.</summary>
    public enum GroupTheme
    {
        /// <summary>Follow the Windows app theme.</summary>
        System = 0,
        Light = 1,
        Dark = 2,
        /// <summary>Mica backdrop where the OS supports it, solid elsewhere.</summary>
        Mica = 3,
        /// <summary>Acrylic backdrop where the OS supports it, solid elsewhere.</summary>
        Acrylic = 4,
        /// <summary>User supplied colours.</summary>
        Custom = 5
    }

    /// <summary>How shortcuts are ordered inside a group popup.</summary>
    public enum GroupSortMode
    {
        /// <summary>The order the user arranged them in.</summary>
        Manual = 0,
        /// <summary>Re-evaluated from rules each time the group is opened.</summary>
        Automatic = 1,
        NameAscending = 2,
        NameDescending = 3,
        ShortcutType = 4
    }

    /// <summary>Which monitor a shortcut wants to be on, or where a popup wants to appear.</summary>
    public enum MonitorPreference
    {
        /// <summary>Wherever the cursor or invoking window currently is.</summary>
        FollowCursor = 0,
        Primary = 1,
        /// <summary>The monitor that was used last for this group.</summary>
        LastUsed = 2,
        /// <summary>A monitor chosen by stable device name in settings.</summary>
        Specific = 3
    }

    /// <summary>Window state requested when a shortcut or workspace item starts.</summary>
    public enum WindowStatePreference
    {
        Normal = 0,
        Minimized = 1,
        Maximized = 2
    }

    /// <summary>Health of a stored shortcut, decided without launching it.</summary>
    public enum ShortcutStatus
    {
        /// <summary>Target resolves and exists.</summary>
        Valid = 0,
        /// <summary>Target no longer exists at the recorded location.</summary>
        Missing = 1,
        /// <summary>Target exists but cannot be read with the current privileges.</summary>
        PermissionDenied = 2,
        /// <summary>The file is not a valid shortcut for the declared type.</summary>
        InvalidShortcut = 3,
        /// <summary>Not enough information to decide.</summary>
        Unknown = 4,
        /// <summary>A shell-activated item whose target is verified only at launch.</summary>
        UriLauncher = 5,
        /// <summary>Item is switched off in the editor and is skipped by launch-all.</summary>
        Disabled = 6
    }

    /// <summary>Orientation of a monitor's own rectangle.</summary>
    public enum ScreenOrientation
    {
        Landscape = 0,
        Portrait = 1,
        LandscapeFlipped = 2,
        PortraitFlipped = 3,
        Unknown = 4
    }

    /// <summary>Where the taskbar is docked on a monitor.</summary>
    public enum TaskbarPosition
    {
        /// <summary>Auto-hidden; the monitor has no reserved area.</summary>
        AutoHide = 0,
        None = 1,
        Left = 2,
        Top = 3,
        Right = 4,
        Bottom = 5
    }

    /// <summary>Coarse category assigned by application discovery.</summary>
    public enum AppCategory
    {
        Development = 0,
        Gaming = 1,
        Office = 2,
        Internet = 3,
        Media = 4,
        Graphics = 5,
        Communication = 6,
        System = 7,
        Utilities = 8,
        Education = 9,
        Other = 10
    }

    /// <summary>Why a launch attempt failed. Stable strings, safe to log and to localise.</summary>
    public enum LaunchErrorCode
    {
        None = 0,
        /// <summary>Target string was empty.</summary>
        EmptyTarget = 1,
        /// <summary>Target file/directory does not exist.</summary>
        TargetNotFound = 2,
        /// <summary>No launcher is registered for the resolved type.</summary>
        UnsupportedType = 3,
        /// <summary>The OS refused the request, usually UAC was declined.</summary>
        PermissionDenied = 4,
        /// <summary>No shell handler is registered for the URI/extension.</summary>
        NoAssociatedHandler = 5,
        /// <summary>Something in the resolution or start path threw.</summary>
        Exception = 6,
        /// <summary>Launching was blocked because the item is disabled.</summary>
        Disabled = 7,
        /// <summary>Script launching is switched off in settings.</summary>
        ScriptsDisabled = 8,
        /// <summary>The shell reported success but no process appeared.</summary>
        NoProcessStarted = 9
    }

    /// <summary>Health-check outcomes surfaced on the diagnostics page.</summary>
    public enum HealthStatus
    {
        Healthy = 0,
        Warning = 1,
        Problem = 2,
        Unknown = 3
    }
}