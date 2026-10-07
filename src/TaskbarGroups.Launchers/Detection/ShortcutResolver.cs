using System;
using System.Collections.Generic;
using System.IO;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Windows.Shell;

namespace TaskbarGroups.Launchers.Detection
{
    /// <summary>
    /// Turns a raw path or URI into a complete, validated <see cref="ShortcutItem"/>.
    /// </summary>
    /// <remarks>
    /// This is the single place a "what did the user just drop" question is
    /// answered. The editor calls it, drag and drop calls it, and the JSON importer
    /// calls it, so a shortcut created three different ways ends up with the same
    /// type, the same working directory and the same warnings.
    /// </remarks>
    public sealed class ShortcutResolver : IShortcutResolver
    {
        public ShortcutType DetectType(string target)
        {
            return TargetClassifier.Classify(target);
        }

        public string? ResolveLinkTarget(string linkPath)
        {
            if (string.IsNullOrWhiteSpace(linkPath)) return null;
            if (!File.Exists(linkPath)) return null;
            return ShellLinkReader.GetTargetPath(linkPath);
        }

        public ResolvedShortcut Resolve(string target, string? displayName = null)
        {
            var result = new ResolvedShortcut();

            if (string.IsNullOrWhiteSpace(target))
            {
                result.Warnings.Add("No target was supplied.");
                return result;
            }

            string trimmed = target.Trim();
            ShortcutType type = TargetClassifier.Classify(trimmed);
            result.Type = type;

            switch (type)
            {
                case ShortcutType.Uwp:
                    result.Target = NormalizeAppUserModelId(trimmed);
                    result.DisplayName = string.IsNullOrWhiteSpace(displayName) ? trimmed : displayName!;
                    break;

                case ShortcutType.Url:
                    result.Target = trimmed;
                    result.DisplayName = string.IsNullOrWhiteSpace(displayName) ? DeriveUriName(trimmed) : displayName!;
                    break;

                case ShortcutType.Steam:
                case ShortcutType.Epic:
                case ShortcutType.EA:
                case ShortcutType.Ubisoft:
                case ShortcutType.Gog:
                case ShortcutType.BattleNet:
                case ShortcutType.Xbox:
                    result.Target = trimmed;
                    result.DisplayName = string.IsNullOrWhiteSpace(displayName) ? trimmed : displayName!;
                    break;

                case ShortcutType.Folder:
                    result.Target = StringHelpers.ExpandPath(trimmed);
                    result.DisplayName = string.IsNullOrWhiteSpace(displayName)
                        ? new DirectoryInfo(result.Target).Name
                        : displayName!;
                    result.SuggestedWorkingDirectory = result.Target;
                    result.Exists = Directory.Exists(result.Target);
                    break;

                default:
                    ResolveFileBacked(ref result, trimmed, displayName);
                    break;
            }

            if (!type.IsShellActivated() && type != ShortcutType.Folder)
            {
                if (!File.Exists(result.Target))
                {
                    result.Warnings.Add("Target does not exist: " + result.Target);
                }
                else if (result.SuggestedWorkingDirectory.Length == 0)
                {
                    result.SuggestedWorkingDirectory = Path.GetDirectoryName(result.Target) ?? string.Empty;
                }
            }

            if (type.ExecutesCode())
            {
                result.Warnings.Add("This shortcut runs a script. It will be executed when the item is launched.");
            }

            if (type == ShortcutType.Lnk)
            {
                ShellLinkInfo info = ShellLinkReader.ReadAll(result.Target);
                if (!info.IsValid)
                {
                    result.Warnings.Add("This .lnk could not be read by Windows and may be corrupt.");
                }
                else if (string.IsNullOrWhiteSpace(info.TargetPath))
                {
                    result.Warnings.Add("This shortcut has no usable target; it may point at something that was removed.");
                }
                else if (!Directory.Exists(info.TargetPath) && !File.Exists(info.TargetPath) && !info.TargetIsUri)
                {
                    result.Warnings.Add("The target of this shortcut is missing: " + info.TargetPath);
                }

                if (info.Arguments.Length > 0 && string.IsNullOrWhiteSpace(displayName))
                {
                    result.DisplayName = ShellLinkReader.GetDisplayName(result.Target);
                }
            }

            result.IsValid = result.Warnings.Count == 0
                             || (result.Warnings.Count == 1 && result.Warnings[0].StartsWith("This shortcut runs a script", StringComparison.Ordinal));

            return result;
        }

        private static void ResolveFileBacked(ref ResolvedShortcut result, string trimmed, string? displayName)
        {
            string expanded = StringHelpers.ExpandPath(trimmed);
            result.Target = expanded;
            result.Exists = File.Exists(expanded);
            result.SuggestedWorkingDirectory = Path.GetDirectoryName(expanded) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(displayName))
            {
                result.DisplayName = ShellLinkReader.GetDisplayName(expanded);
            }
            else
            {
                result.DisplayName = displayName!;
            }
        }

        private static string NormalizeAppUserModelId(string raw)
        {
            string value = raw.Trim();

            const string appsFolder = "shell:appsFolder\\";
            if (value.StartsWith(appsFolder, StringComparison.OrdinalIgnoreCase))
                return value.Substring(appsFolder.Length).Trim();

            if (value.StartsWith("shell:appsFolder/", StringComparison.OrdinalIgnoreCase))
                return value.Substring("shell:appsFolder/".Length).Trim();

            if (value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                return value.Substring("shell:".Length).Trim();

            return value;
        }

        private static string DeriveUriName(string uri)
        {
            try
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
                {
                    if (!string.IsNullOrWhiteSpace(parsed.Host) && parsed.Scheme != "file")
                        return parsed.Host;

                    string name = Path.GetFileNameWithoutExtension(parsed.LocalPath);
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
            }
            catch (Exception)
            {
            }

            return uri;
        }

        /// <summary>Converts a resolution into a storable item.</summary>
        public ShortcutItem ToItem(ResolvedShortcut resolved, Guid groupId, int sortOrder)
        {
            if (resolved == null) throw new ArgumentNullException(nameof(resolved));

            return new ShortcutItem
            {
                GroupId = groupId,
                Name = resolved.DisplayName,
                Type = resolved.Type,
                Target = resolved.Target,
                WorkingDirectory = resolved.SuggestedWorkingDirectory,
                SortOrder = sortOrder
            };
        }

        /// <summary>Resolves several targets at once, skipping the ones that cannot be used.</summary>
        public List<ShortcutItem> ResolveMany(IEnumerable<string> targets, Guid groupId, Action<string>? onSkipped = null)
        {
            var items = new List<ShortcutItem>();
            if (targets == null) return items;

            int order = 0;
            foreach (string target in targets)
            {
                ResolvedShortcut resolved = Resolve(target);
                if (resolved.Type == ShortcutType.Unknown)
                {
                    onSkipped?.Invoke(target);
                    continue;
                }

                items.Add(ToItem(resolved, groupId, order++));
            }

            return items;
        }
    }
}