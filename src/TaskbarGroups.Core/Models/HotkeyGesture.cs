using System;
using System.Collections.Generic;
using System.Text;
using TaskbarGroups.Core.Validation;

namespace TaskbarGroups.Core.Models
{
    /// <summary>
    /// A keyboard shortcut, parsed from and rendered to a human-readable string.
    /// </summary>
    /// <remarks>
    /// Windows Forms has <c>Keys</c> but nothing that turns "Ctrl+Alt+G" into one,
    /// and the settings page needs both directions. Parsing lives in Core so it can
    /// be unit tested without a message loop; registering the actual hotkey stays
    /// in the platform layer.
    /// </remarks>
    public sealed class HotkeyGesture
    {
        public const int NoKey = 0;

        private HotkeyGesture(int modifiers, int keyCode, string display)
        {
            Modifiers = modifiers;
            KeyCode = keyCode;
            Display = display;
        }

        public int Modifiers { get; }

        /// <summary>A Win32 virtual key code.</summary>
        public int KeyCode { get; }

        /// <summary>Canonical text form, e.g. "Ctrl+Alt+G".</summary>
        public string Display { get; }

        public bool IsValid => KeyCode != NoKey && !string.IsNullOrEmpty(Display);

        private const int ModAlt = 0x0001;
        private const int ModControl = 0x0002;
        private const int ModShift = 0x0004;
        private const int ModWin = 0x0008;

        private static readonly Dictionary<string, int> NamedKeys =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "Backspace", 0x08 }, { "Tab", 0x09 }, { "Enter", 0x0D }, { "Return", 0x0D },
                { "Shift", 0x10 }, { "Ctrl", 0x11 }, { "Control", 0x11 }, { "Alt", 0x12 },
                { "Pause", 0x13 }, { "CapsLock", 0x14 }, { "Escape", 0x1B }, { "Esc", 0x1B },
                { "Space", 0x20 }, { "PageUp", 0x21 }, { "PageDown", 0x22 }, { "End", 0x23 },
                { "Home", 0x24 }, { "Left", 0x25 }, { "Up", 0x26 }, { "Right", 0x27 },
                { "Down", 0x28 }, { "Insert", 0x2D }, { "Delete", 0x2E },
                { "D0", 0x30 }, { "D1", 0x31 }, { "D2", 0x32 }, { "D3", 0x33 }, { "D4", 0x34 },
                { "D5", 0x35 }, { "D6", 0x36 }, { "D7", 0x37 }, { "D8", 0x38 }, { "D9", 0x39 },
                { "A", 0x41 }, { "B", 0x42 }, { "C", 0x43 }, { "D", 0x44 }, { "E", 0x45 },
                { "F", 0x46 }, { "G", 0x47 }, { "H", 0x48 }, { "I", 0x49 }, { "J", 0x4A },
                { "K", 0x4B }, { "L", 0x4C }, { "M", 0x4D }, { "N", 0x4E }, { "O", 0x4F },
                { "P", 0x50 }, { "Q", 0x51 }, { "R", 0x52 }, { "S", 0x53 }, { "T", 0x54 },
                { "U", 0x55 }, { "V", 0x56 }, { "W", 0x57 }, { "X", 0x58 }, { "Y", 0x59 },
                { "Z", 0x5A },
                { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 }, { "F5", 0x74 },
                { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 }, { "F9", 0x78 }, { "F10", 0x79 },
                { "F11", 0x7A }, { "F12", 0x7B },
                { "NumPad0", 0x60 }, { "NumPad1", 0x61 }, { "NumPad2", 0x62 }, { "NumPad3", 0x63 },
                { "NumPad4", 0x64 }, { "NumPad5", 0x65 }, { "NumPad6", 0x66 }, { "NumPad7", 0x67 },
                { "NumPad8", 0x68 }, { "NumPad9", 0x69 },
                { "Multiply", 0x6A }, { "Add", 0x6B }, { "Subtract", 0x6D }, { "Decimal", 0x6E },
                { "Divide", 0x6F }
            };

        /// <summary>True when the gesture uses only modifiers plus one non-modifier key.</summary>
        public static bool IsParseable(string? gesture)
        {
            return TryParse(gesture, out _);
        }

        /// <summary>
        /// Parses "Ctrl+Alt+G". Returns false with an empty result for anything
        /// malformed or for a gesture that has no non-modifier key, because
        /// registering that would either fail or swallow the whole keyboard.
        /// </summary>
        public static bool TryParse(string? gesture, out HotkeyGesture result)
        {
            result = new HotkeyGesture(0, NoKey, string.Empty);
            if (string.IsNullOrWhiteSpace(gesture)) return false;

            int modifiers = 0;
            int key = NoKey;
            var canonical = new List<string>();

            foreach (string rawPart in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                string part = rawPart.Trim();
                if (part.Length == 0) continue;

                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModControl;
                    canonical.Add("Ctrl");
                    continue;
                }
                if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModAlt;
                    canonical.Add("Alt");
                    continue;
                }
                if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModShift;
                    canonical.Add("Shift");
                    continue;
                }
                if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModWin;
                    canonical.Add("Win");
                    continue;
                }

                if (!TryResolveKey(part, out key))
                    return false;
            }

            if (key == NoKey)
                return false;

            result = new HotkeyGesture(modifiers, key, BuildDisplay(modifiers, key));
            return true;
        }

        private static bool TryResolveKey(string part, out int keyCode)
        {
            keyCode = NoKey;

            if (NamedKeys.TryGetValue(part, out keyCode))
                return keyCode != NoKey;

            // Single printable characters, including non-ASCII letters, which are
            // reachable as VK codes on a standard keyboard layout.
            if (part.Length == 1)
            {
                char c = char.ToUpperInvariant(part[0]);
                if (c >= 'A' && c <= 'Z')
                {
                    keyCode = c;
                    return true;
                }
                if (c >= '0' && c <= '9')
                {
                    keyCode = c;
                    return true;
                }
            }

            return false;
        }

        private static string BuildDisplay(int modifiers, int key)
        {
            var parts = new List<string>(4);
            if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
            if ((modifiers & ModAlt) != 0) parts.Add("Alt");
            if ((modifiers & ModShift) != 0) parts.Add("Shift");
            if ((modifiers & ModWin) != 0) parts.Add("Win");
            parts.Add(KeyName(key));
            return string.Join("+", parts);
        }

        private static string KeyName(int keyCode)
        {
            foreach (var pair in NamedKeys)
            {
                if (pair.Value != keyCode) continue;
                // Prefer the digit letter form over D0..D9 and Ctrl over Control.
                if (keyCode >= 0x41 && keyCode <= 0x5A) return ((char)keyCode).ToString();
                if (keyCode >= 0x30 && keyCode <= 0x39) return ((char)keyCode).ToString();
                return pair.Key;
            }
            return "0x" + keyCode.ToString("X2");
        }

        public bool HasModifier(int modifier) => (Modifiers & modifier) != 0;

        public bool IsControl => (Modifiers & ModControl) != 0;

        public bool IsAlt => (Modifiers & ModAlt) != 0;

        public bool IsShift => (Modifiers & ModShift) != 0;

        public bool IsWin => (Modifiers & ModWin) != 0;

        public override string ToString() => Display;

        /// <summary>Canonicalises a stored gesture, or returns null when unusable.</summary>
        public static string? Normalize(string? gesture)
        {
            return TryParse(gesture, out HotkeyGesture parsed) ? parsed.Display : null;
        }

        /// <summary>
        /// Finds a gesture that clashes with <paramref name="candidate"/> among
        /// <paramref name="inUse"/>, so the settings page can warn instead of
        /// silently failing at registration time.
        /// </summary>
        public static string? FindConflict(string candidate, IEnumerable<string> inUse)
        {
            if (!TryParse(candidate, out HotkeyGesture parsed)) return null;
            if (inUse == null) return null;

            foreach (string used in inUse)
            {
                if (string.IsNullOrWhiteSpace(used)) continue;
                if (!TryParse(used, out HotkeyGesture other)) continue;
                if (other.Modifiers == parsed.Modifiers && other.KeyCode == parsed.KeyCode)
                    return other.Display;
            }

            return null;
        }

        /// <summary>Default gestures the group popup understands.</summary>
        public static class Defaults
        {
            /// <summary>Legacy: the first ten shortcuts are launched with 1..0.</summary>
            public const string LaunchNth = "numeric";

            /// <summary>Legacy: Ctrl+Enter launches everything when the group allows it.</summary>
            public const string OpenAll = "Ctrl+Enter";

            public const string MainWindow = "Ctrl+Alt+G";
        }
    }

    /// <summary>
    /// The sort orders a group popup can apply.
    /// </summary>
    public sealed class ShortcutSortHelper
    {
        /// <summary>
        /// Returns the display order for a group. <see cref="Enums.GroupSortMode.Automatic"/>
        /// puts running processes first so the shortcuts the user just launched are
        /// at the top, which is the only automatic ordering that is useful without
        /// asking the user for rules.
        /// </summary>
        public static IReadOnlyList<Models.ShortcutItem> Order(
            IReadOnlyList<Models.ShortcutItem> items,
            Enums.GroupSortMode mode,
            Func<Models.ShortcutItem, bool>? isRunning = null)
        {
            if (items == null || items.Count == 0) return items ?? Array.Empty<Models.ShortcutItem>();

            switch (mode)
            {
                case Enums.GroupSortMode.NameAscending:
                    return Sort(items, (a, b) => string.Compare(GetName(a), GetName(b), StringComparison.CurrentCultureIgnoreCase));

                case Enums.GroupSortMode.NameDescending:
                    return Sort(items, (a, b) => string.Compare(GetName(b), GetName(a), StringComparison.CurrentCultureIgnoreCase));

                case Enums.GroupSortMode.ShortcutType:
                    return Sort(items, (a, b) => a.Type.CompareTo(b.Type));

                case Enums.GroupSortMode.Automatic:
                    if (isRunning == null)
                        return Sort(items, (a, b) => a.SortOrder.CompareTo(b.SortOrder));
                    return Sort(items, (a, b) =>
                    {
                        int byRunning = isRunning(b).CompareTo(isRunning(a));
                        return byRunning != 0 ? byRunning : a.SortOrder.CompareTo(b.SortOrder);
                    });

                default:
                    return Sort(items, (a, b) => a.SortOrder.CompareTo(b.SortOrder));
            }
        }

        private static IReadOnlyList<Models.ShortcutItem> Sort(IReadOnlyList<Models.ShortcutItem> items, Comparison<Models.ShortcutItem> comparison)
        {
            var copy = new List<Models.ShortcutItem>(items);
            copy.Sort((x, y) =>
            {
                int result = comparison(x, y);
                return result != 0 ? result : x.SortOrder.CompareTo(y.SortOrder);
            });
            return copy;
        }

        private static string GetName(Models.ShortcutItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Name)) return item.Name;
            if (string.IsNullOrWhiteSpace(item.Target)) return string.Empty;
            int slash = Math.Max(item.Target.LastIndexOf('\\'), item.Target.LastIndexOf('/'));
            return slash >= 0 ? item.Target.Substring(slash + 1) : item.Target;
        }
    }

    /// <summary>Open-all behaviour shared by the popup and the dashboard.</summary>
    public sealed class OpenAllOptions
    {
        public int DelayMs { get; set; } = 120;

        public bool SkipDisabled { get; set; } = true;

        public bool SkipMissing { get; set; } = true;

        public int ConfirmationThreshold { get; set; } = 10;

        public static OpenAllOptions FromSettings(AppSettings settings)
        {
            return new OpenAllOptions
            {
                ConfirmationThreshold = settings.OpenAllWarningThreshold,
                DelayMs = 120
            };
        }
    }
}