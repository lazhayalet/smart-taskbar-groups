using System;
using System.Collections.Generic;

namespace TaskbarGroups.Core.Models
{
    /// <summary>
    /// The versioned JSON document used for import, export and backup.
    /// </summary>
    /// <remarks>
    /// Names mirror the wire format exactly with camelCase members, so this
    /// doubles as the serialisation contract. Everything is nullable-tolerant on
    /// the way in: an export from 1.x-era data or from an older 2.x build must
    /// still import.
    /// </remarks>
    public sealed class ExportDocument
    {
        public ExportDocument()
        {
            Groups = new List<ExportGroup>();
            Workspaces = new List<ExportWorkspace>();
            Themes = new List<ThemeDefinition>();
            Settings = new AppSettings();
        }

        public int SchemaVersion { get; set; } = Constants.ExportSchema.CurrentVersion;

        public string ApplicationVersion { get; set; } = string.Empty;

        public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Non-fatal notes, e.g. icons that were not embedded.</summary>
        public List<string> Notes { get; set; } = new List<string>();

        public List<ExportGroup> Groups { get; set; }

        public List<ExportWorkspace> Workspaces { get; set; }

        public List<ThemeDefinition> Themes { get; set; }

        public AppSettings Settings { get; set; }
    }

    public sealed class ExportGroup
    {
        public ExportGroup()
        {
            Shortcuts = new List<ExportShortcut>();
            Name = string.Empty;
            Description = string.Empty;
            IconId = string.Empty;
        }

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>Key into the application's icon store.</summary>
        public string IconId { get; set; }

        /// <summary>Base64 PNG of the group icon, present only when icons are included.</summary>
        public string? IconBase64 { get; set; }

        public int Width { get; set; }

        public Enums.GroupTheme Theme { get; set; }

        public double Opacity { get; set; }

        public int CornerRadius { get; set; }

        public bool ShadowEnabled { get; set; }

        public bool BlurEnabled { get; set; }

        public bool AnimationEnabled { get; set; }

        public bool OpenAllEnabled { get; set; }

        public Enums.GroupSortMode SortMode { get; set; }

        public Enums.MonitorPreference MonitorPreference { get; set; }

        public string PreferredMonitorDeviceName { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public List<ExportShortcut> Shortcuts { get; set; }

        /// <summary>Reconstructs a domain group.</summary>
        public Group ToDomain()
        {
            return new Group
            {
                Id = Id,
                Name = Name,
                Description = Description,
                IconId = string.IsNullOrEmpty(IconId) && IconBase64 != null ? "embedded:" + Id.ToString("N") : IconId,
                Width = Width,
                Theme = Theme,
                Opacity = Opacity,
                CornerRadius = CornerRadius,
                ShadowEnabled = ShadowEnabled,
                BlurEnabled = BlurEnabled,
                AnimationEnabled = AnimationEnabled,
                OpenAllEnabled = OpenAllEnabled,
                SortMode = SortMode,
                MonitorPreference = MonitorPreference,
                PreferredMonitorDeviceName = PreferredMonitorDeviceName,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt
            };
        }
    }

    public sealed class ExportShortcut
    {
        public ExportShortcut()
        {
            Name = string.Empty;
            Target = string.Empty;
            Arguments = string.Empty;
            WorkingDirectory = string.Empty;
            IconSource = string.Empty;
            PreferredMonitorDeviceName = string.Empty;
        }

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; }

        public Enums.ShortcutType Type { get; set; }

        public string Target { get; set; }

        public string Arguments { get; set; }

        public string WorkingDirectory { get; set; }

        public string IconSource { get; set; }

        public bool RunAsAdministrator { get; set; }

        public Enums.WindowStatePreference WindowState { get; set; }

        public Enums.MonitorPreference PreferredMonitor { get; set; }

        public string PreferredMonitorDeviceName { get; set; }

        public bool Enabled { get; set; }

        public int SortOrder { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        /// <summary>Reconstructs a domain shortcut belonging to <paramref name="group"/>.</summary>
        public ShortcutItem ToDomain(Guid group)
        {
            return new ShortcutItem
            {
                Id = Id,
                GroupId = group,
                Name = Name,
                Type = Type,
                Target = Target,
                Arguments = Arguments,
                WorkingDirectory = WorkingDirectory,
                IconSource = IconSource,
                IconCacheKey = string.Empty,
                RunAsAdministrator = RunAsAdministrator,
                WindowState = WindowState,
                PreferredMonitor = PreferredMonitor,
                PreferredMonitorDeviceName = PreferredMonitorDeviceName,
                Enabled = Enabled,
                SortOrder = SortOrder,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt
            };
        }
    }

    public sealed class ExportWorkspace
    {
        public ExportWorkspace()
        {
            Name = string.Empty;
            Description = string.Empty;
            IconId = string.Empty;
            Items = new List<ExportWorkspaceItem>();
        }

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; }

        public string Description { get; set; }

        public string IconId { get; set; }

        public int LaunchDelayMs { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public List<ExportWorkspaceItem> Items { get; set; }

        public List<WindowPlacement> Placements { get; set; } = new List<WindowPlacement>();

        public Workspace ToDomain()
        {
            var workspace = new Workspace
            {
                Id = Id,
                Name = Name,
                Description = Description,
                IconId = IconId,
                LaunchDelayMs = LaunchDelayMs,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt
            };

            foreach (var item in Items)
            {
                var domainItem = item.ToDomain();
                domainItem.WorkspaceId = workspace.Id;
                workspace.Items.Add(domainItem);
            }

            return workspace;
        }
    }

    public sealed class ExportWorkspaceItem
    {
        public ExportWorkspaceItem()
        {
            ProcessMatch = string.Empty;
            Executable = string.Empty;
            MonitorDeviceName = string.Empty;
        }

        public Guid Id { get; set; } = Guid.NewGuid();

        public string ProcessMatch { get; set; }

        public string Executable { get; set; }

        public string MonitorDeviceName { get; set; }

        public int X { get; set; }

        public int Y { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public Enums.WindowStatePreference WindowState { get; set; }

        public PlacementAnchor NormalizedPlacement { get; set; }

        public int LaunchDelayMs { get; set; }

        public int SortOrder { get; set; }

        public WorkspaceItem ToDomain()
        {
            return new WorkspaceItem
            {
                Id = Id,
                ProcessMatch = ProcessMatch,
                Executable = Executable,
                MonitorDeviceName = MonitorDeviceName,
                X = X,
                Y = Y,
                Width = Width,
                Height = Height,
                WindowState = WindowState,
                NormalizedPlacement = NormalizedPlacement,
                LaunchDelayMs = LaunchDelayMs,
                SortOrder = SortOrder
            };
        }
    }
}