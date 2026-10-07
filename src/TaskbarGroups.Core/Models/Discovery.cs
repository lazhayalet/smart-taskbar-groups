using System;
using System.Collections.Generic;
using TaskbarGroups.Core.Enums;

namespace TaskbarGroups.Core.Models
{
    /// <summary>
    /// An application found on this machine by the discovery pipeline.
    /// </summary>
    /// <remarks>
    /// Discovery never touches a database; it returns records, and the caller
    /// decides whether to turn one into a shortcut. That keeps a scan cancellable
    /// and testable.
    /// </remarks>
    public sealed class DiscoveredApplication
    {
        public DiscoveredApplication()
        {
            Id = string.Empty;
            DisplayName = string.Empty;
            Target = string.Empty;
            Source = DiscoverySource.Unknown;
            Type = ShortcutType.Unknown;
            Category = AppCategory.Other;
        }

        /// <summary>Stable hash of source+target, used to deduplicate across scans.</summary>
        public string Id { get; set; }

        public string DisplayName { get; set; }

        /// <summary>Path or URI that launches this application.</summary>
        public string Target { get; set; }

        public DiscoverySource Source { get; set; }

        public ShortcutType Type { get; set; }

        public AppCategory Category { get; set; }

        /// <summary>Icon file or URL the icon providers can use directly.</summary>
        public string IconHint { get; set; } = string.Empty;

        /// <summary>Publisher or vendor, when the source exposes it.</summary>
        public string Publisher { get; set; } = string.Empty;

        /// <summary>Set when this record came from a rule-based smart group.</summary>
        public bool IsAutoDetected { get; set; }
    }

    /// <summary>Where a discovered application record came from.</summary>
    public enum DiscoverySource
    {
        Unknown = 0,
        StartMenu = 1,
        UserStartMenu = 2,
        Desktop = 3,
        ProgramFiles = 4,
        ProgramFilesX86 = 5,
        LocalAppDataPrograms = 6,
        UwpPackage = 7,
        Pwa = 8,
        Steam = 9,
        RegisteredAppPaths = 10,
        RegistryUninstall = 11
    }

    /// <summary>Options for one discovery pass.</summary>
    public sealed class DiscoveryOptions
    {
        public bool IncludeStartMenu { get; set; } = true;

        public bool IncludeDesktop { get; set; } = true;

        public bool IncludeProgramFiles { get; set; } = true;

        public bool IncludeUwp { get; set; } = true;

        public bool IncludePwa { get; set; } = true;

        public bool IncludeSteam { get; set; }

        public bool FollowNetworkRoots { get; set; }

        public int MaxResultsPerSource { get; set; } = 2000;
    }

    /// <summary>One line on the diagnostics page.</summary>
    public sealed class HealthCheckResult
    {
        public HealthCheckResult()
        {
            Name = string.Empty;
            Detail = string.Empty;
        }

        public string Name { get; set; }

        public HealthStatus Status { get; set; }

        public string Detail { get; set; }

        /// <summary>Action the user can take, when the status is not Healthy.</summary>
        public string SuggestedAction { get; set; } = string.Empty;
    }

    /// <summary>Everything the diagnostics page and the exported report need.</summary>
    public sealed class DiagnosticReport
    {
        public DiagnosticReport()
        {
            GeneratedAt = DateTimeOffset.Now;
            Checks = new List<HealthCheckResult>();
            Monitors = new List<MonitorInfo>();
            LogPaths = new List<string>();
        }

        public string ApplicationVersion { get; set; } = string.Empty;

        public string OsDescription { get; set; } = string.Empty;

        public string OsArchitecture { get; set; } = string.Empty;

        public string ProcessArchitecture { get; set; } = string.Empty;

        public bool IsPortable { get; set; }

        public string DataDirectory { get; set; } = string.Empty;

        public bool DpiAware { get; set; }

        public string DpiAwarenessContext { get; set; } = string.Empty;

        public List<MonitorInfo> Monitors { get; }

        public List<HealthCheckResult> Checks { get; }

        public ShortcutValidationReport? Shortcuts { get; set; }

        public List<string> LogPaths { get; }

        public DateTimeOffset GeneratedAt { get; set; }
    }

    /// <summary>One entry in the migration log that accompanies a legacy import.</summary>
    public sealed class MigrationReportEntry
    {
        public MigrationReportEntry()
        {
            Stage = string.Empty;
            Message = string.Empty;
        }

        public string Stage { get; set; }

        public string Message { get; set; }

        public MigrationSeverity Severity { get; set; }

        public string GroupName { get; set; } = string.Empty;
    }

    public enum MigrationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>What a legacy import did, so the user is told and nothing is lost silently.</summary>
    public sealed class MigrationReport
    {
        public MigrationReport()
        {
            StartedAt = DateTimeOffset.UtcNow;
            Entries = new List<MigrationReportEntry>();
            GroupsImported = new List<string>();
            ShortcutsImported = 0;
            ShortcutsSkipped = 0;
            UnsupportedFields = new List<string>();
        }

        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset? CompletedAt { get; set; }

        public bool Succeeded { get; set; }

        public int GroupsImportedCount { get; set; }

        public List<string> GroupsImported { get; }

        public int ShortcutsImported { get; set; }

        public int ShortcutsSkipped { get; set; }

        /// <summary>
        /// Legacy properties with no equivalent in the new model. Reported, never
        /// discarded silently.
        /// </summary>
        public List<string> UnsupportedFields { get; }

        public List<MigrationReportEntry> Entries { get; }

        /// <summary>Path of the ZIP written before any change was made.</summary>
        public string BackupArchivePath { get; set; } = string.Empty;

        /// <summary>Root the legacy data was read from.</summary>
        public string LegacyRoot { get; set; } = string.Empty;

        public void Add(MigrationSeverity severity, string stage, string message, string group = "")
        {
            Entries.Add(new MigrationReportEntry
            {
                Severity = severity,
                Stage = stage,
                Message = message,
                GroupName = group ?? string.Empty
            });
        }

        public void Complete(bool success)
        {
            Succeeded = success;
            CompletedAt = DateTimeOffset.UtcNow;
            GroupsImportedCount = GroupsImported.Count;
        }
    }
}