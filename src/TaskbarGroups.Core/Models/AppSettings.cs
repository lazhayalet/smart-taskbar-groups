using System;
using System.Collections.Generic;

namespace TaskbarGroups.Core.Models
{
    /// <summary>
    /// Everything the user can configure that is not per-group.
    /// </summary>
    /// <remarks>
    /// Stored as a single JSON document in the database rather than as scattered
    /// registry entries, so a backup is complete and portable. Defaults here are
    /// the behaviour a fresh install gets; migration overrides them explicitly.
    /// </remarks>
    public sealed class AppSettings
    {
        public const int CurrentSchemaVersion = 2;

        public AppSettings()
        {
            SchemaVersion = CurrentSchemaVersion;

            // Startup
            StartWithWindows = false;
            StartMinimized = false;
            StartInTray = true;

            // Taskbar / group popup
            GroupWidth = 5;
            GroupOpacity = 10d;
            GroupTheme = Enums.GroupTheme.System;
            GroupCornerRadius = 8;
            GroupShadowEnabled = true;
            GroupBlurEnabled = true;
            GroupAnimationEnabled = true;
            GroupPopupOffset = 10;
            GroupPopupGap = 20;
            GroupIconSize = 32;
            PopupOpenDelayMs = 0;

            // Launchers
            ConfirmBeforeOpenAll = false;
            OpenAllWarningThreshold = 10;
            EnableScriptLaunching = true;
            ShowScriptWarning = true;
            DefaultRunAsAdministrator = false;

            // Icons
            IconCacheEnabled = true;
            PreferredIconSize = 32;
            FallbackIconName = "fallback";
            AllowRemoteFavicons = false;

            // Discovery
            DiscoveryScanProgramFilesOnStartup = false;
            DiscoveryIncludeSteam = false;

            // Workspaces
            WorkspaceDefaultLaunchDelayMs = 750;
            WorkspacePlacementTimeoutMs = 8000;

            // Backup
            AutomaticBackupEnabled = true;
            AutomaticBackupIntervalHours = 24;
            BackupRotationCount = 10;

            // Updates
            CheckForUpdatesOnStartup = false;
            UpdateFeedUrl = "https://api.github.com/repos/tjackenpacken/taskbar-groups/releases/latest";

            // Hotkeys
            ShowGroupHotkey = "Ctrl+Alt+G";
            EnableGlobalHotkeys = true;

            // Diagnostics
            LoggingEnabled = true;
            LogLevel = "Information";
            VerboseLogging = false;

            // Explorer integration
            ExplorerContextMenuEnabled = true;

            // Offline behaviour
            OfflineMode = false;
        }

        public int SchemaVersion { get; set; }

        // ---- Startup -------------------------------------------------------
        public bool StartWithWindows { get; set; }
        public bool StartMinimized { get; set; }
        public bool StartInTray { get; set; }

        // ---- Taskbar / group popup -----------------------------------------
        public int GroupWidth { get; set; }
        public double GroupOpacity { get; set; }
        public Enums.GroupTheme GroupTheme { get; set; }
        public int GroupCornerRadius { get; set; }
        public bool GroupShadowEnabled { get; set; }
        public bool GroupBlurEnabled { get; set; }
        public bool GroupAnimationEnabled { get; set; }

        /// <summary>Gap between the taskbar edge and the popup, in logical pixels.</summary>
        public int GroupPopupOffset { get; set; }

        /// <summary>Gap between the cursor and the popup when opened above the taskbar, in logical pixels.</summary>
        public int GroupPopupGap { get; set; }

        public int GroupIconSize { get; set; }
        public int PopupOpenDelayMs { get; set; }

        // ---- Launchers ------------------------------------------------------
        public bool ConfirmBeforeOpenAll { get; set; }
        public int OpenAllWarningThreshold { get; set; }
        public bool EnableScriptLaunching { get; set; }
        public bool ShowScriptWarning { get; set; }
        public bool DefaultRunAsAdministrator { get; set; }

        // ---- Icons ----------------------------------------------------------
        public bool IconCacheEnabled { get; set; }
        public int PreferredIconSize { get; set; }
        public string FallbackIconName { get; set; }
        public bool AllowRemoteFavicons { get; set; }

        // ---- Discovery ------------------------------------------------------
        public bool DiscoveryScanProgramFilesOnStartup { get; set; }
        public bool DiscoveryIncludeSteam { get; set; }

        // ---- Workspaces -----------------------------------------------------
        public int WorkspaceDefaultLaunchDelayMs { get; set; }
        public int WorkspacePlacementTimeoutMs { get; set; }

        // ---- Backup ---------------------------------------------------------
        public bool AutomaticBackupEnabled { get; set; }
        public int AutomaticBackupIntervalHours { get; set; }
        public int BackupRotationCount { get; set; }

        // ---- Updates --------------------------------------------------------
        public bool CheckForUpdatesOnStartup { get; set; }
        public string UpdateFeedUrl { get; set; }

        // ---- Hotkeys --------------------------------------------------------
        public bool EnableGlobalHotkeys { get; set; }

        /// <summary>Global hotkey that opens the main window. Parseable with <c>HotkeyGesture.Parse</c>.</summary>
        public string ShowGroupHotkey { get; set; }

        // ---- Diagnostics ----------------------------------------------------
        public bool LoggingEnabled { get; set; }
        public string LogLevel { get; set; }
        public bool VerboseLogging { get; set; }

        // ---- Explorer integration ------------------------------------------
        public bool ExplorerContextMenuEnabled { get; set; }

        /// <summary>
        /// When true the application never touches the network. Core group and
        /// workspace features work regardless of this flag.
        /// </summary>
        public bool OfflineMode { get; set; }

        // ---- Localization ----------------------------------------------------
        /// <summary>UI language: "en" or "tr".</summary>
        public string Language { get; set; } = "en";

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }
    }

    /// <summary>
    /// One theme preset plus user overrides, persisted with the settings.
    /// </summary>
    public sealed class ThemeDefinition
    {
        public ThemeDefinition()
        {
            Name = "System";
            Base = Enums.GroupTheme.System;
            BackgroundColor = "#FF1F1F1F";
            ForegroundColor = "#FFFFFFFF";
            AccentColor = "#FF4CC2FF";
            HoverColor = "#FF323232";
            SelectionColor = "#FF2A2A2A";
            Opacity = 10d;
            CornerRadius = 8;
            IconSize = 32;
            RowSpacing = 8;
            ColumnSpacing = 8;
            ShadowEnabled = true;
        }

        public string Name { get; set; }

        public Enums.GroupTheme Base { get; set; }

        public string BackgroundColor { get; set; }

        public string ForegroundColor { get; set; }

        public string AccentColor { get; set; }

        public string HoverColor { get; set; }

        public string SelectionColor { get; set; }

        public double Opacity { get; set; }

        public int CornerRadius { get; set; }

        public int IconSize { get; set; }

        public int RowSpacing { get; set; }

        public int ColumnSpacing { get; set; }

        public bool ShadowEnabled { get; set; }

        public static ThemeDefinition BuiltIn(Enums.GroupTheme theme)
        {
            switch (theme)
            {
                case Enums.GroupTheme.Light:
                    return new ThemeDefinition
                    {
                        Name = "Light",
                        Base = theme,
                        BackgroundColor = "#FFE6E6E6",
                        ForegroundColor = "#FF1B1B1B",
                        HoverColor = "#FFD2D2D2",
                        SelectionColor = "#FFC4C4C4"
                    };

                case Enums.GroupTheme.Dark:
                    return new ThemeDefinition
                    {
                        Name = "Dark",
                        Base = theme,
                        BackgroundColor = "#FF1F1F1F",
                        ForegroundColor = "#FFF3F3F3",
                        HoverColor = "#FF323232",
                        SelectionColor = "#FF2A2A2A"
                    };

                case Enums.GroupTheme.Mica:
                case Enums.GroupTheme.Acrylic:
                    var mica = new ThemeDefinition
                    {
                        Name = theme.ToString(),
                        Base = theme,
                        BackgroundColor = "#DD202020",
                        ForegroundColor = "#FFF3F3F3",
                        HoverColor = "#30FFFFFF",
                        SelectionColor = "#40FFFFFF",
                        CornerRadius = 8
                    };
                    return mica;

                default:
                    return new ThemeDefinition();
            }
        }

        public ThemeDefinition Clone() => (ThemeDefinition)MemberwiseClone();
    }

    /// <summary>
    /// Where the application keeps its data, resolved once at start-up.
    /// </summary>
    /// <remarks>
    /// The legacy build wrote <c>config\</c>, <c>Shortcuts\</c> and
    /// <c>JITComp\</c> relative to the process working directory. That breaks as
    /// soon as the exe is launched from a shortcut whose "Start in" is something
    /// else, and it cannot work at all under Program Files without elevation.
    /// This type decides once, explicitly, where data goes.
    /// </remarks>
    public sealed class DataPaths
    {
        public DataPaths(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
                throw new ArgumentException("A data directory is required.", nameof(baseDirectory));

            BaseDirectory = baseDirectory;
            DatabaseFile = System.IO.Path.Combine(baseDirectory, "TaskbarGroups.db");
            IconsDirectory = System.IO.Path.Combine(baseDirectory, "Icons");
            CacheDirectory = System.IO.Path.Combine(baseDirectory, "Cache");
            BackupsDirectory = System.IO.Path.Combine(baseDirectory, "Backups");
            LogsDirectory = System.IO.Path.Combine(baseDirectory, "Logs");
            TempDirectory = System.IO.Path.Combine(baseDirectory, "Temp");
            LegacyConfigDirectory = System.IO.Path.Combine(baseDirectory, "config");
            LegacyShortcutsDirectory = System.IO.Path.Combine(baseDirectory, "Shortcuts");
        }

        public string BaseDirectory { get; }

        public string DatabaseFile { get; }

        public string IconsDirectory { get; }

        public string CacheDirectory { get; }

        public string BackupsDirectory { get; }

        public string LogsDirectory { get; }

        public string TempDirectory { get; }

        /// <summary>The pre-2.0 <c>config\&lt;group&gt;</c> folders, read but never deleted.</summary>
        public string LegacyConfigDirectory { get; }

        /// <summary>The pre-2.0 <c>Shortcuts\</c> folder, still written so pinned taskbar entries keep working.</summary>
        public string LegacyShortcutsDirectory { get; }

        public void EnsureCreated()
        {
            foreach (string dir in new[] { BaseDirectory, IconsDirectory, CacheDirectory, BackupsDirectory, LogsDirectory, TempDirectory })
            {
                System.IO.Directory.CreateDirectory(dir);
            }
        }

        public bool IsWritable()
        {
            try
            {
                string probe = System.IO.Path.Combine(BaseDirectory, "write-probe.tmp");
                System.IO.File.WriteAllText(probe, "ok");
                System.IO.File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}