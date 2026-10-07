using System;
using System.Collections.Generic;
using TaskbarGroups.Core.Enums;

namespace TaskbarGroups.Core.Models
{
    /// <summary>
    /// A named arrangement of applications that open together, on chosen
    /// monitors, at chosen window positions.
    /// </summary>
    public sealed class Workspace
    {
        public Workspace()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            Description = string.Empty;
            IconId = string.Empty;
            LaunchDelayMs = 750;
            CreatedAt = DateTimeOffset.UtcNow;
            UpdatedAt = CreatedAt;
        }

        public Guid Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public string IconId { get; set; }

        /// <summary>
        /// Delay between each launch, in milliseconds. A single delay cannot
        /// guarantee that a heavyweight app is up before its dependent app is
        /// started, but it covers the common "two terminals, then the browser"
        /// case; per-item delays are available on <see cref="WorkspaceItem.DelayMs"/>.
        /// </summary>
        public int LaunchDelayMs { get; set; }

        public List<WorkspaceItem> Items { get; set; } = new List<WorkspaceItem>();

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

        public Workspace Clone()
        {
            var clone = (Workspace)MemberwiseClone();
            clone.Id = Guid.NewGuid();
            clone.Items = new List<WorkspaceItem>();
            foreach (var item in Items)
            {
                var itemClone = item.Clone();
                itemClone.WorkspaceId = clone.Id;
                clone.Items.Add(itemClone);
            }
            return clone;
        }
    }

    /// <summary>One application inside a workspace.</summary>
    public sealed class WorkspaceItem
    {
        public WorkspaceItem()
        {
            Id = Guid.NewGuid();
            ProcessMatch = string.Empty;
            Executable = string.Empty;
            MonitorDeviceName = string.Empty;
            Width = 0;
            Height = 0;
            WindowState = WindowStatePreference.Normal;
            NormalizedPlacement = PlacementAnchor.Center;
            LaunchDelayMs = 0;
            SortOrder = 0;
        }

        public Guid Id { get; set; }

        public Guid WorkspaceId { get; set; }

        /// <summary>
        /// How the target window is found after launch: an executable file name,
        /// or the window title. Executable matching is preferred because titles
        /// change and are localised.
        /// </summary>
        public string ProcessMatch { get; set; }

        /// <summary>Shortcut to launch. Empty means "attach to an already running window".</summary>
        public string Executable { get; set; }

        /// <summary>Stable monitor device name; empty means the primary monitor.</summary>
        public string MonitorDeviceName { get; set; }

        /// <summary>Requested left edge in DIPs. 0 means "use the anchor".</summary>
        public int X { get; set; }

        /// <summary>Requested top edge in DIPs. 0 means "use the anchor".</summary>
        public int Y { get; set; }

        /// <summary>Requested width in DIPs. 0 means "keep the window's own size".</summary>
        public int Width { get; set; }

        /// <summary>Requested height in DIPs. 0 means "keep the window's own size".</summary>
        public int Height { get; set; }

        public WindowStatePreference WindowState { get; set; }

        /// <summary>Fractional/edge placement used when X/Y are not set.</summary>
        public PlacementAnchor NormalizedPlacement { get; set; }

        /// <summary>Extra delay before this item, on top of the workspace delay.</summary>
        public int LaunchDelayMs { get; set; }

        public int SortOrder { get; set; }

        public WorkspaceItem Clone()
        {
            var clone = (WorkspaceItem)MemberwiseClone();
            clone.Id = Guid.NewGuid();
            return clone;
        }
    }

    /// <summary>Where on a monitor a window goes when no explicit coordinates are given.</summary>
    public enum PlacementAnchor
    {
        Center = 0,
        Left = 1,
        Right = 2,
        Top = 3,
        Bottom = 4,
        TopLeft = 5,
        TopRight = 6,
        BottomLeft = 7,
        BottomRight = 8,
        /// <summary>Left half of the work area.</summary>
        LeftHalf = 9,
        /// <summary>Right half of the work area.</summary>
        RightHalf = 10,
        /// <summary>Top half of the work area.</summary>
        TopHalf = 11,
        /// <summary>Bottom half of the work area.</summary>
        BottomHalf = 12
    }

    /// <summary>
    /// A saved window rectangle for a process, used to restore the layout that
    /// was on screen when the workspace was recorded.
    /// </summary>
    public sealed class WindowPlacement
    {
        public WindowPlacement()
        {
            ProcessMatch = string.Empty;
            Executable = string.Empty;
            MonitorId = string.Empty;
            X = 0;
            Y = 0;
            Width = 800;
            Height = 600;
            WindowState = WindowStatePreference.Normal;
        }

        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Window title or class substring, when matching by window.</summary>
        public string ProcessMatch { get; set; }

        /// <summary>Executable file name, when matching by process.</summary>
        public string Executable { get; set; }

        /// <summary>Stable monitor device name the window was on.</summary>
        public string MonitorId { get; set; }

        /// <summary>Left edge in physical pixels, in virtual-screen coordinates.</summary>
        public int X { get; set; }

        public int Y { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public WindowStatePreference WindowState { get; set; }

        public Guid? WorkspaceId { get; set; }

        public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}