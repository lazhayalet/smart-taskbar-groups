using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TaskbarGroups.Core.Validation
{
    /// <summary>
    /// The one place a shortcut's link target is read from, injected by the
    /// Windows layer at start-up.
    /// </summary>
    /// <remarks>
    /// Reading a .lnk needs <c>IShellLinkW</c>, which is Windows interop and
    /// therefore cannot live in Core. Core still needs to classify a .lnk as
    /// valid/moved/broken without knowing anything about COM, so it asks through
    /// this delegate. When nothing has registered a resolver - a unit test, or a
    /// non-Windows build - classification falls back to "the file exists", which
    /// is the most it can honestly claim.
    /// </remarks>
    public static class LinkResolver
    {
        private static Func<string, string?>? _resolver;

        public static void Register(Func<string, string?> resolver)
        {
            _resolver = resolver;
        }

        public static bool IsRegistered => _resolver != null;

        public static string? TryResolve(string linkPath)
        {
            if (_resolver == null || string.IsNullOrWhiteSpace(linkPath)) return null;
            try
            {
                return _resolver(linkPath);
            }
            catch (Exception)
            {
                // A resolver that throws must not turn a diagnostic check into a
                // crash; report "unresolvable" and let the UI offer a repair.
                return null;
            }
        }
    }
    /// <summary>
    /// Why a value was rejected, in terms the UI can show next to the field.
    /// </summary>
    public sealed class ValidationResult
    {
        private ValidationResult(bool isValid, string? message)
        {
            IsValid = isValid;
            Message = message;
        }

        public bool IsValid { get; }

        /// <summary>Null when valid.</summary>
        public string? Message { get; }

        public static readonly ValidationResult Valid = new ValidationResult(true, null);

        public static ValidationResult Invalid(string message) => new ValidationResult(false, message);

        public override string ToString() => IsValid ? "valid" : Message ?? "invalid";
    }

    /// <summary>
    /// Rules for group names.
    /// </summary>
    /// <remarks>
    /// The legacy editor used two different transforms on the same string: it
    /// replaced whitespace with underscores to make the folder name, and
    /// replaced underscores with spaces to make the .lnk name. Names are validated
    /// once here so the two can no longer drift.
    /// </remarks>
    public static class GroupNameValidator
    {
        public const int MaxLength = 49;

        private static readonly Regex AllowedCharacters =
            new Regex(@"^[0-9a-zA-Z \b]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex InvalidFileNameCharacters =
            new Regex(@"[*""'<>|\\/:*?]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static ValidationResult Validate(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return ValidationResult.Invalid("Must select a name");

            if (name.Length > MaxLength)
                return ValidationResult.Invalid("Name must be " + MaxLength + " characters or fewer");

            if (name.Trim().Length == 0)
                return ValidationResult.Invalid("Must select a name");

            // The legacy rule rejected anything outside [0-9a-zA-Z ] which also
            // blocked non-ASCII names. That check is kept for compatibility with
            // folders created by 1.x, but the file-system safety rule is the one
            // that actually has to hold, so it is checked separately and reported
            // more precisely.
            if (InvalidFileNameCharacters.IsMatch(name))
                return ValidationResult.Invalid("Name must not have any of: * \" ' < > | \\ / : ?");

            foreach (char c in name)
            {
                if (char.IsControl(c))
                    return ValidationResult.Invalid("Name must not have any of: * \" ' < > | \\ / : ?");
            }

            return ValidationResult.Valid;
        }

        /// <summary>True when the name uses only characters 1.x accepted.</summary>
        public static bool IsLegacyCompatible(string? name)
        {
            return !string.IsNullOrEmpty(name) && AllowedCharacters.IsMatch(name);
        }

        /// <summary>Folder-safe form: whitespace becomes underscores.</summary>
        public static string ToStorageName(string name)
        {
            return Regex.Replace(name.Trim(), @"\s+", "_");
        }

        /// <summary>Display form: underscores become spaces.</summary>
        public static string ToDisplayName(string name)
        {
            return Regex.Replace(name, @"(_)+", " ").Trim();
        }
    }

    /// <summary>
    /// Rules for shortcut items before they are stored.
    /// </summary>
    /// <remarks>
    /// The legacy editor accepted anything the file dialog returned and only
    /// discovered the problem at launch time. Validation here is what lets the
    /// diagnostics page report a broken shortcut without launching it.
    /// </remarks>
    public static class ShortcutValidator
    {
        public static ValidationResult Validate(Models.ShortcutItem? item)
        {
            if (item == null)
                return ValidationResult.Invalid("Missing shortcut");

            if (string.IsNullOrWhiteSpace(item.Target))
                return ValidationResult.Invalid("Target must not be empty");

            switch (item.Type)
            {
                case Enums.ShortcutType.Exe:
                case Enums.ShortcutType.Bat:
                case Enums.ShortcutType.Cmd:
                case Enums.ShortcutType.Ps1:
                    return ValidateFileTarget(item.Target);

                case Enums.ShortcutType.Lnk:
                case Enums.ShortcutType.AppRefMs:
                    return ValidateFileTarget(item.Target);

                case Enums.ShortcutType.Folder:
                    return ValidateFolderTarget(item.Target);

                case Enums.ShortcutType.Url:
                case Enums.ShortcutType.Steam:
                case Enums.ShortcutType.Epic:
                case Enums.ShortcutType.EA:
                case Enums.ShortcutType.Ubisoft:
                case Enums.ShortcutType.Gog:
                case Enums.ShortcutType.BattleNet:
                case Enums.ShortcutType.Xbox:
                case Enums.ShortcutType.Pwa:
                    return ValidateUriTarget(item.Target);

                case Enums.ShortcutType.Uwp:
                case Enums.ShortcutType.Msix:
                    return ValidateAppUserModelId(item.Target);

                default:
                    return ValidationResult.Invalid("Shortcut type could not be determined");
            }
        }

        public static ValidationResult ValidateFileTarget(string target)
        {
            if (!Path.IsPathRooted(target))
                return ValidationResult.Invalid("Target must be an absolute path: " + target);

            if (Directory.Exists(target))
                return ValidationResult.Invalid("Target is a folder, not a file: " + target);

            if (!File.Exists(target))
                return ValidationResult.Invalid("Target does not exist: " + target);

            return ValidationResult.Valid;
        }

        public static ValidationResult ValidateFolderTarget(string target)
        {
            if (!Path.IsPathRooted(target))
                return ValidationResult.Invalid("Target must be an absolute path: " + target);

            if (!Directory.Exists(target))
                return ValidationResult.Invalid("Folder does not exist: " + target);

            return ValidationResult.Valid;
        }

        public static ValidationResult ValidateUriTarget(string target)
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
                return ValidationResult.Invalid("Not a valid URI: " + target);

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                // Protocol URIs such as steam:// are legitimate, so only reject
                // schemes that cannot be dispatched at all.
                if (uri.Scheme.IndexOf('.') < 0 && uri.Scheme.Length < 3)
                    return ValidationResult.Invalid("Not a valid URI: " + target);
            }

            return ValidationResult.Valid;
        }

        public static ValidationResult ValidateAppUserModelId(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return ValidationResult.Invalid("Missing application identifier");

            if (target.IndexOf('!') < 0)
                return ValidationResult.Invalid("Not an application identifier: " + target);

            return ValidationResult.Valid;
        }

        /// <summary>
        /// Decides a shortcut's status without launching it. Never throws.
        /// </summary>
        public static Enums.ShortcutStatus Classify(Models.ShortcutItem item)
        {
            if (item == null) return Enums.ShortcutStatus.Unknown;
            if (!item.Enabled) return Enums.ShortcutStatus.Disabled;

            string target = item.Target ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target)) return Enums.ShortcutStatus.Unknown;

            try
            {
                switch (item.Type)
                {
                    case Enums.ShortcutType.Uwp:
                    case Enums.ShortcutType.Msix:
                        // Packaged app membership cannot be checked without a
                        // package enumeration; the launcher will report failure.
                        return Enums.ShortcutStatus.UriLauncher;

                    case Enums.ShortcutType.Url:
                    case Enums.ShortcutType.Pwa:
                    case Enums.ShortcutType.Steam:
                    case Enums.ShortcutType.Epic:
                    case Enums.ShortcutType.EA:
                    case Enums.ShortcutType.Ubisoft:
                    case Enums.ShortcutType.Gog:
                    case Enums.ShortcutType.BattleNet:
                    case Enums.ShortcutType.Xbox:
                        return ValidateUriTarget(target).IsValid
                            ? Enums.ShortcutStatus.UriLauncher
                            : Enums.ShortcutStatus.Missing;

                    case Enums.ShortcutType.Folder:
                        return Directory.Exists(target) ? Enums.ShortcutStatus.Valid : Enums.ShortcutStatus.Missing;

                    case Enums.ShortcutType.Lnk:
                        return ClassifyLink(item);

                    case Enums.ShortcutType.Exe:
                    case Enums.ShortcutType.Bat:
                    case Enums.ShortcutType.Cmd:
                    case Enums.ShortcutType.Ps1:
                        if (Directory.Exists(target)) return Enums.ShortcutStatus.InvalidShortcut;
                        if (!File.Exists(target)) return Enums.ShortcutStatus.Missing;
                        return ProbeReadable(target);

                    default:
                        return Enums.ShortcutStatus.Unknown;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return Enums.ShortcutStatus.PermissionDenied;
            }
            catch (IOException)
            {
                return Enums.ShortcutStatus.Missing;
            }
            catch (Exception)
            {
                return Enums.ShortcutStatus.Unknown;
            }
        }

        private static Enums.ShortcutStatus ClassifyLink(Models.ShortcutItem item)
        {
            if (!File.Exists(item.Target)) return Enums.ShortcutStatus.Missing;

            Enums.ShortcutStatus readable = ProbeReadable(item.Target);
            if (readable == Enums.ShortcutStatus.PermissionDenied) return readable;

            // A .lnk that exists but does not resolve is the "moved target" case
            // users hit most often, so it gets its own status.
            string? linkTarget = LinkResolver.TryResolve(item.Target);
            if (string.IsNullOrWhiteSpace(linkTarget)) return Enums.ShortcutStatus.InvalidShortcut;

            if (Directory.Exists(linkTarget)) return Enums.ShortcutStatus.Valid;
            if (!File.Exists(linkTarget)) return Enums.ShortcutStatus.Missing;
            return Enums.ShortcutStatus.Valid;
        }

        private static Enums.ShortcutStatus ProbeReadable(string path)
        {
            try
            {
                using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    return stream.CanRead ? Enums.ShortcutStatus.Valid : Enums.ShortcutStatus.PermissionDenied;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return Enums.ShortcutStatus.PermissionDenied;
            }
            catch (FileNotFoundException)
            {
                return Enums.ShortcutStatus.Missing;
            }
            catch (DirectoryNotFoundException)
            {
                return Enums.ShortcutStatus.Missing;
            }
            catch (IOException)
            {
                // Locked by another process; not necessarily broken.
                return Enums.ShortcutStatus.Valid;
            }
        }
    }

    /// <summary>
    /// Validates a whole group before it is written.
    /// </summary>
    public static class GroupValidator
    {
        public const int MaxShortcutsPerGroup = 20;
        public const int MaxWidth = 19;
        public const int MinWidth = 1;

        /// <summary>Returns one message per problem; empty means the group is saveable.</summary>
        public static IReadOnlyList<string> Validate(Models.Group group, IReadOnlyList<Models.ShortcutItem> shortcuts)
        {
            var problems = new List<string>();

            if (group == null)
            {
                problems.Add("Missing group");
                return problems;
            }

            ValidationResult name = GroupNameValidator.Validate(group.Name);
            if (!name.IsValid && name.Message != null) problems.Add(name.Message);

            if (shortcuts == null || shortcuts.Count == 0)
            {
                problems.Add("Must select at least one shortcut");
            }
            else if (shortcuts.Count > MaxShortcutsPerGroup)
            {
                problems.Add("Max " + MaxShortcutsPerGroup + " shortcuts in one group");
            }

            if (group.Width < MinWidth || group.Width > MaxWidth)
                problems.Add("Width must be between " + MinWidth + " and " + MaxWidth);

            if (group.Opacity < 0d || group.Opacity > 100d)
                problems.Add("Opacity must be between 0 and 100");

            if (group.CornerRadius < 0 || group.CornerRadius > 32)
                problems.Add("Corner radius must be between 0 and 32");

            return problems;
        }
    }

    /// <summary>Shared string utilities with no other home.</summary>
    public static class StringHelpers
    {
        /// <summary>
        /// Quotes a single argument for <c>CommandLineToArgvW</c>. Quoting an
        /// already-quoted argument is left alone so a user-typed argument string
        /// is not mangled.
        /// </summary>
        public static string QuoteArgument(string? argument)
        {
            if (string.IsNullOrEmpty(argument)) return "\"\"";

            if (argument.Length > 1 && argument[0] == '"' && argument[argument.Length - 1] == '"')
                return argument;

            bool needsQuotes = false;
            for (int i = 0; i < argument.Length; i++)
            {
                char c = argument[i];
                if (c == '"' || char.IsWhiteSpace(c) || c == '\t' || c == '\n' || c == '\v')
                {
                    needsQuotes = true;
                    break;
                }
            }

            if (!needsQuotes) return argument;

            var builder = new System.Text.StringBuilder(argument.Length + 8);
            builder.Append('"');
            for (int i = 0; i < argument.Length; i++)
            {
                int backslashes = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == argument.Length)
                {
                    // Backslashes before the closing quote must be doubled.
                    builder.Append('\\', backslashes * 2);
                    break;
                }

                if (argument[i] == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(argument[i]);
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>Expands environment variables and normalises a path without throwing.</summary>
        public static string ExpandPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            string expanded;
            try
            {
                expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            }
            catch (ArgumentException)
            {
                // Malformed %VAR% sequences.
                expanded = path.Trim().Trim('"');
            }

            try
            {
                return Path.GetFullPath(expanded);
            }
            catch (Exception)
            {
                return expanded;
            }
        }

        /// <summary>True when the string has no characters the file system rejects.</summary>
        public static bool IsSafeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;
        }

        /// <summary>
        /// Makes a string safe for use as a file name without destroying
        /// non-ASCII characters, which the legacy icon cache stripped out.
        /// </summary>
        public static string SanitizeFileName(string? name, int maxLength = 96)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unnamed";

            var invalid = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars());
            var builder = new System.Text.StringBuilder(name.Length);
            foreach (char c in name.Trim())
            {
                builder.Append(invalid.Contains(c) ? '_' : c);
            }

            string result = builder.ToString().Trim();
            if (result.Length == 0) return "unnamed";
            if (result.Length > maxLength) result = result.Substring(0, maxLength);
            return result;
        }
    }
}