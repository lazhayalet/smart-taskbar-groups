using System;
using System.Collections.Generic;
using System.Linq;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;

namespace TaskbarGroups.Groups.SmartGroups
{
    /// <summary>How a rule decides whether an application belongs in a group.</summary>
    public enum SmartGroupMode
    {
        /// <summary>The user picks applications by hand.</summary>
        Manual = 0,

        /// <summary>Everything matching the rule, refreshed on demand.</summary>
        Automatic = 1,

        /// <summary>Everything matching, minus what the user removed.</summary>
        RuleBased = 2
    }

    /// <summary>A rule that selects applications for a group.</summary>
    public sealed class SmartGroupRule
    {
        public SmartGroupRule()
        {
            Name = string.Empty;
        }

        public string Name { get; set; }

        /// <summary>Categories that count as a match. Empty matches all.</summary>
        public List<AppCategory> Categories { get; set; } = new List<AppCategory>();

        /// <summary>Substrings matched against name and target, case-insensitively.</summary>
        public List<string> Keywords { get; set; } = new List<string>();

        /// <summary>Shortcut types that count as a match. Empty matches all.</summary>
        public List<ShortcutType> Types { get; set; } = new List<ShortcutType>();

        public SmartGroupMode Mode { get; set; } = SmartGroupMode.Manual;

        /// <summary>
        /// Applications the user removed from an automatic group. A rule-based
        /// group honours these; an automatic one does not, which is the difference
        /// between the two modes.
        /// </summary>
        public List<string> ExcludedTargets { get; set; } = new List<string>();

        public bool Matches(DiscoveredApplication application)
        {
            if (application == null) return false;

            if (ExcludedTargets != null && ExcludedTargets.Contains(application.Target ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                return false;

            bool anyCriterion = false;

            if (Categories != null && Categories.Count > 0)
            {
                anyCriterion = true;
                if (!Categories.Contains(application.Category)) return false;
            }

            if (Types != null && Types.Count > 0)
            {
                anyCriterion = true;
                if (!Types.Contains(application.Type)) return false;
            }

            if (Keywords != null && Keywords.Count > 0)
            {
                anyCriterion = true;

                string haystack = (application.DisplayName + " " + application.Target).ToLowerInvariant();
                bool hit = false;
                foreach (string keyword in Keywords)
                {
                    if (!string.IsNullOrWhiteSpace(keyword) &&
                        haystack.Contains(keyword.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                    {
                        hit = true;
                        break;
                    }
                }

                if (!hit) return false;
            }

            // A rule with no criteria matches everything, which is the useful
            // behaviour for "all Steam games" expressed as a type filter alone.
            return anyCriterion || true;
        }
    }

    /// <summary>
    /// Builds and refreshes rule-driven groups.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A smart group is a normal group whose membership is computed. That matters:
    /// the popup, the taskbar shortcut and the editor all work exactly as they do
    /// for a manual group, because there is no separate code path for them.
    /// </para>
    /// <para>
    /// Manual override is respected. In <see cref="SmartGroupMode.RuleBased"/> the
    /// user can remove anything the rule produced, and the exclusion survives the
    /// next refresh. In <see cref="SmartGroupMode.Automatic"/> the rule is
    /// authoritative and the group is rebuilt each time - which is the mode to pick
    /// for "everything Steam installs", where a manual exclusion would just come
    /// back.
    /// </para>
    /// </remarks>
    public sealed class SmartGroupService
    {
        private readonly IGroupRepository _groups;
        private readonly IShortcutResolver _resolver;
        private readonly IAppLogger? _logger;

        public SmartGroupService(IGroupRepository groups, IShortcutResolver resolver, IAppLogger? logger = null)
        {
            _groups = groups ?? throw new ArgumentNullException(nameof(groups));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _logger = logger;
        }

        /// <summary>Rules offered in the "create smart group" flow.</summary>
        public IReadOnlyList<SmartGroupRule> PresetRules()
        {
            return new List<SmartGroupRule>
            {
                new SmartGroupRule
                {
                    Name = "Development tools",
                    Categories = new List<AppCategory> { AppCategory.Development },
                    Mode = SmartGroupMode.RuleBased
                },
                new SmartGroupRule
                {
                    Name = "Steam library",
                    Types = new List<ShortcutType> { ShortcutType.Steam },
                    Mode = SmartGroupMode.Automatic
                },
                new SmartGroupRule
                {
                    Name = "Browser web apps",
                    Types = new List<ShortcutType> { ShortcutType.Pwa },
                    Mode = SmartGroupMode.RuleBased
                },
                new SmartGroupRule
                {
                    Name = "Microsoft Office",
                    Keywords = new List<string> { "word", "excel", "powerpoint", "outlook", "onenote", "office", "access", "visio" },
                    Mode = SmartGroupMode.RuleBased
                },
                new SmartGroupRule
                {
                    Name = "Games from other stores",
                    Types = new List<ShortcutType>
                    {
                        ShortcutType.Epic, ShortcutType.EA, ShortcutType.Ubisoft,
                        ShortcutType.Gog, ShortcutType.BattleNet, ShortcutType.Xbox
                    },
                    Mode = SmartGroupMode.RuleBased
                },
                new SmartGroupRule
                {
                    Name = "Media and graphics",
                    Categories = new List<AppCategory> { AppCategory.Media, AppCategory.Graphics },
                    Mode = SmartGroupMode.RuleBased
                },
                new SmartGroupRule
                {
                    Name = "Communication",
                    Categories = new List<AppCategory> { AppCategory.Communication },
                    Mode = SmartGroupMode.RuleBased
                },
                new SmartGroupRule
                {
                    Name = "Utilities and system",
                    Categories = new List<AppCategory> { AppCategory.Utilities, AppCategory.System },
                    Mode = SmartGroupMode.RuleBased
                }
            };
        }

        /// <summary>
        /// Builds the shortcut items a rule selects from a discovery result.
        /// </summary>
        /// <remarks>
        /// The legacy 20-shortcut cap applies. A rule like "all Steam games" can
        /// match hundreds of titles, and a group popup is a grid: past a couple of
        /// dozen entries it stops being usable. Excess entries are reported rather
        /// than silently dropped, so the user knows to refine the rule.
        /// </remarks>
        public List<ShortcutItem> BuildShortcuts(
            SmartGroupRule rule,
            IReadOnlyList<DiscoveredApplication> discovered,
            Guid groupId,
            List<string>? notes = null)
        {
            var items = new List<ShortcutItem>();
            if (rule == null || discovered == null) return items;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int order = 0;

            foreach (DiscoveredApplication application in discovered)
            {
                if (application == null) continue;
                if (!rule.Matches(application)) continue;

                if (!seen.Add(application.Target ?? string.Empty)) continue;

                if (items.Count >= Core.Validation.GroupValidator.MaxShortcutsPerGroup)
                {
                    notes?.Add("The rule matched more than " + Core.Validation.GroupValidator.MaxShortcutsPerGroup +
                               " applications; only the first " + Core.Validation.GroupValidator.MaxShortcutsPerGroup +
                               " were added. Refine the rule to see the rest.");
                    break;
                }

                ResolvedShortcut resolved = _resolver.Resolve(application.Target ?? string.Empty, application.DisplayName);

                items.Add(new ShortcutItem
                {
                    GroupId = groupId,
                    Name = application.DisplayName,
                    Type = resolved.Type != ShortcutType.Unknown ? resolved.Type : application.Type,
                    Target = resolved.Type != ShortcutType.Unknown ? (resolved.Target ?? string.Empty) : (application.Target ?? string.Empty),
                    Arguments = string.Empty,
                    WorkingDirectory = resolved.SuggestedWorkingDirectory,
                    IconSource = application.IconHint ?? string.Empty,
                    Enabled = true,
                    SortOrder = order++,
                    IsAutoDetected = false
                });
            }

            return items;
        }

        /// <summary>
        /// Creates a group from a rule. The result is an ordinary group, editable
        /// like any other.
        /// </summary>
        public Group CreateFromRule(
            SmartGroupRule rule,
            IReadOnlyList<DiscoveredApplication> discovered,
            List<string>? notes = null)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));

            var group = new Group
            {
                Name = string.IsNullOrWhiteSpace(rule.Name) ? "Smart group" : rule.Name.Trim(),
                Description = "Built from a rule; edit the shortcuts to refine it.",
                SortMode = GroupSortMode.NameAscending,
                OpenAllEnabled = false
            };

            _groups.Upsert(group);

            List<ShortcutItem> items = BuildShortcuts(rule, discovered, group.Id, notes);
            _groups.ReplaceShortcuts(group.Id, items);

            _logger?.Log(SystemLogLevel.Information, "App",
                "Created smart group '" + group.Name + "' with " + items.Count + " shortcuts");

            return group;
        }

        /// <summary>
        /// Refreshes a rule-based group: adds anything newly matching and keeps
        /// every exclusion the user made.
        /// </summary>
        public int Refresh(Group group, SmartGroupRule rule, IReadOnlyList<DiscoveredApplication> discovered)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (rule == null) throw new ArgumentNullException(nameof(rule));

            List<ShortcutItem> desired = BuildShortcuts(rule, discovered, group.Id);

            if (rule.Mode == SmartGroupMode.Automatic)
            {
                _groups.ReplaceShortcuts(group.Id, desired);
                return desired.Count;
            }

            // Rule-based: only add. Anything the user removed stays removed, and
            // anything they reordered keeps its place.
            List<ShortcutItem> existing = _groups.GetShortcuts(group.Id);
            var keptTargets = new HashSet<string>(
                existing.Select(s => s.Target ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            var additions = desired
                .Where(s => !keptTargets.Contains(s.Target ?? string.Empty))
                .ToList();

            if (additions.Count == 0) return 0;

            int nextOrder = existing.Count;
            foreach (ShortcutItem addition in additions) addition.SortOrder = nextOrder++;

            _groups.ReplaceShortcuts(group.Id, existing.Concat(additions).ToList());
            return additions.Count;
        }
    }
}