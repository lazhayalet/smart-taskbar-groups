using System;

namespace TaskbarGroups.Core.Constants
{
    /// <summary>
    /// Values that must stay identical to what Taskbar Groups 1.x produced.
    /// </summary>
    /// <remarks>
    /// A user who pinned a group to the taskbar before 2.0 has a shortcut whose
    /// AppUserModelID is <c>tjackenpacken.taskbarGroup.menu.&lt;group&gt;</c> and
    /// whose argument 1 is the group name. Windows matches the pinned entry to a
    /// running process through exactly that pair, so changing either value would
    /// silently detach every existing user's pinned groups. These are contract,
    /// not preference.
    /// </remarks>
    public static class AppContract
    {
        /// <summary>Prefix of the per-group AppUserModelID. Do not change.</summary>
        public const string GroupAppIdPrefix = "tjackenpacken.taskbarGroup.menu.";

        /// <summary>AppUserModelID of the main dashboard window.</summary>
        public const string MainAppId = "tjackenpacken.taskbarGroup.main";

        /// <summary>Legacy folder name inside the data directory.</summary>
        public const string LegacyConfigFolderName = "config";

        /// <summary>Legacy shortcuts folder. Still written so pinned entries survive.</summary>
        public const string LegacyShortcutsFolderName = "Shortcuts";

        /// <summary>Legacy per-group data file name.</summary>
        public const string LegacyObjectDataFileName = "ObjectData.xml";

        /// <summary>Legacy group icon file name.</summary>
        public const string LegacyGroupIconFileName = "GroupIcon.ico";

        /// <summary>Legacy group image file name.</summary>
        public const string LegacyGroupImageFileName = "GroupImage.png";

        /// <summary>Legacy icon cache folder name inside a group folder.</summary>
        public const string LegacyIconCacheFolderName = "Icons";

        /// <summary>The group-popup argument that switches the process into popup mode.</summary>
        public const string GroupArgumentName = "--group";

        /// <summary>Per-shortcut cell width in the legacy popup, logical pixels.</summary>
        public const int LegacyShortcutCellWidth = 55;

        /// <summary>Per-row height in the legacy popup, logical pixels.</summary>
        public const int LegacyShortcutCellHeight = 45;

        /// <summary>Builds the AppUserModelID for a group.</summary>
        public static string BuildGroupAppId(string groupName)
        {
            return GroupAppIdPrefix + groupName;
        }
    }

    /// <summary>Physical metrics the legacy popup assumed, kept so migrated groups look the same.</summary>
    public static class LegacyMetrics
    {
        public const int DefaultPopupMargin = 10;
        public const int DefaultCursorGap = 20;
        public const int TaskbarAssumedThickness = 35;
        public const int LegacyTaskbarGap = 45;
        public const int LegacyOpacityMax = 100;
    }

    /// <summary>Names of the rolling log files.</summary>
    public static class LogFileNames
    {
        public const string Application = "application.log";
        public const string Launcher = "launcher.log";
        public const string Migration = "migration.log";
        public const string Diagnostics = "diagnostics.log";
        public const string Crash = "crash.log";
    }

    /// <summary>Export/import document format.</summary>
    public static class ExportSchema
    {
        /// <summary>
        /// Version of the JSON document format. Bumped whenever a field changes
        /// meaning. The importer accepts anything up to this number.
        /// </summary>
        public const int CurrentVersion = 2;
    }
}