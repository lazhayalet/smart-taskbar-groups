using System;
using System.Collections.Generic;
using TaskbarGroups.Core.Enums;

namespace TaskbarGroups.Core.Models
{
    /// <summary>
    /// A taskbar group: the named set of shortcuts shown in one popup window.
    /// </summary>
    /// <remarks>
    /// This replaces the legacy <c>Category</c> class. The legacy class was bound
    /// to <see cref="System.Xml.Serialization.XmlSerializer"/> through public
    /// fields and stored everything relative to the process working directory;
    /// this one is an ordinary persistable record whose identity is a stable
    /// <see cref="Id"/> so a rename does not orphan anything.
    /// </remarks>
    public sealed class Group
    {
        public Group()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            Description = string.Empty;
            CreatedAt = DateTimeOffset.UtcNow;
            UpdatedAt = CreatedAt;
            Theme = GroupTheme.System;
            Opacity = 10d;
            CornerRadius = 8;
            ShadowEnabled = true;
            BlurEnabled = true;
            AnimationEnabled = true;
            OpenAllEnabled = true;
            SortMode = GroupSortMode.Manual;
            MonitorPreference = MonitorPreference.FollowCursor;
            IconId = string.Empty;
            LegacyName = string.Empty;
        }

        /// <summary>Stable identity. Survives renames.</summary>
        public Guid Id { get; set; }

        /// <summary>Display name as typed by the user, spaces preserved.</summary>
        public string Name { get; set; }

        /// <summary>Optional free-text description shown in tooltips.</summary>
        public string Description { get; set; }

        /// <summary>
        /// Key into the icon store for the group icon, or empty when the icon is
        /// still the legacy file on disk (<c>config/&lt;name&gt;/GroupIcon.ico</c>).
        /// </summary>
        public string IconId { get; set; }

        /// <summary>Shortcuts per row, as in the legacy editor's width stepper.</summary>
        public int Width { get; set; } = 5;

        public GroupTheme Theme { get; set; }

        /// <summary>Legacy opacity scale, 0 (opaque) to 100 (transparent).</summary>
        public double Opacity { get; set; }

        /// <summary>Corner radius in logical pixels for the popup window.</summary>
        public int CornerRadius { get; set; }

        public bool ShadowEnabled { get; set; }

        /// <summary>Backdrop blur. Silently degraded to a solid fill where unsupported.</summary>
        public bool BlurEnabled { get; set; }

        public bool AnimationEnabled { get; set; }

        /// <summary>
        /// Legacy <c>allowOpenAll</c>: whether Ctrl+Enter launches every enabled
        /// shortcut. Defaults to true for groups migrated from older versions,
        /// because that checkbox used to start out ticked.
        /// </summary>
        public bool OpenAllEnabled { get; set; }

        public GroupSortMode SortMode { get; set; }

        /// <summary>Stable device name of the monitor this group prefers, when set.</summary>
        public string PreferredMonitorDeviceName { get; set; } = string.Empty;

        public MonitorPreference MonitorPreference { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        /// <summary>
        /// The directory name the legacy install used, kept so a migrated group
        /// can still be recognised and its icon files found.
        /// </summary>
        public string LegacyName { get; set; }

        public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

        public Group Clone()
        {
            return (Group)MemberwiseClone();
        }
    }

    /// <summary>
    /// One entry inside a group.
    /// </summary>
    /// <remarks>
    /// Replaces <c>ProgramShortcut</c>. The important additions are
    /// <see cref="Type"/> (so a folder or a URL is never handed to
    /// <c>CreateProcess</c>), <see cref="Enabled"/>, <see cref="RunAsAdministrator"/>
    /// and <see cref="IconCacheKey"/>, which replaces the legacy
    /// filename-derived cache name.
    /// </remarks>
    public sealed class ShortcutItem
    {
        public ShortcutItem()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            Type = ShortcutType.Unknown;
            Target = string.Empty;
            Arguments = string.Empty;
            WorkingDirectory = string.Empty;
            IconSource = string.Empty;
            IconCacheKey = string.Empty;
            WindowState = WindowStatePreference.Normal;
            PreferredMonitor = MonitorPreference.FollowCursor;
            Enabled = true;
            SortOrder = 0;
            CreatedAt = DateTimeOffset.UtcNow;
            UpdatedAt = CreatedAt;
        }

        public Guid Id { get; set; }

        /// <summary>Owning group. Ignored when the item is stored in its parent's list.</summary>
        public Guid GroupId { get; set; }

        /// <summary>Name shown under the icon. Empty means "derive from the target".</summary>
        public string Name { get; set; }

        public ShortcutType Type { get; set; }

        /// <summary>
        /// What to launch. A filesystem path for file-backed types, a URI for
        /// shell-activated types, an AppUserModelID for packaged apps.
        /// </summary>
        public string Target { get; set; }

        /// <summary>Arguments passed verbatim; quoting is the launcher's job.</summary>
        public string Arguments { get; set; }

        /// <summary>
        /// Working directory. Empty means "let the launcher derive it from the
        /// target", which is what fixes the legacy behaviour where every shortcut
        /// started inside the Taskbar Groups folder.
        /// </summary>
        public string WorkingDirectory { get; set; }

        /// <summary>Explicit icon choice: a file path or <c>builtin:</c> name.</summary>
        public string IconSource { get; set; }

        /// <summary>Content hash of the icon inputs; empty means not yet resolved.</summary>
        public string IconCacheKey { get; set; }

        /// <summary>Elevate on launch. Never set implicitly.</summary>
        public bool RunAsAdministrator { get; set; }

        public WindowStatePreference WindowState { get; set; }

        public MonitorPreference PreferredMonitor { get; set; }

        public string PreferredMonitorDeviceName { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        public int SortOrder { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        /// <summary>
        /// True when this item was added by discovery rather than by hand.
        /// </summary>
        public bool IsAutoDetected { get; set; }

        /// <summary>
        /// Legacy <c>isWindowsApp</c>. Retained so a migrated item is still
        /// recognised as a packaged app if its Type has not been resolved yet.
        /// </summary>
        public bool IsLegacyWindowsApp { get; set; }

        /// <summary>Directory or file name the legacy install used for this item.</summary>
        public string LegacyFilePath { get; set; } = string.Empty;

        public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

        public ShortcutItem Clone()
        {
            var clone = (ShortcutItem)MemberwiseClone();
            clone.Id = Guid.NewGuid();
            return clone;
        }
    }

    /// <summary>What kind of thing a group was built from, for smart groups.</summary>
    public enum GroupOrigin
    {
        /// <summary>Created by the user in the editor.</summary>
        Manual = 0,
        /// <summary>Assembled from discovery results by rules.</summary>
        RuleBased = 1,
        /// <summary>Imported from a JSON export.</summary>
        Imported = 2,
        /// <summary>Produced by the legacy XML migration.</summary>
        Migrated = 3
    }
}