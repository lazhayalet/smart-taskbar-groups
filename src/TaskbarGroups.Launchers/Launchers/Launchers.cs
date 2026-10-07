using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using System.Linq;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Launchers.Abstractions;
using TaskbarGroups.Windows.Shell;

namespace TaskbarGroups.Launchers.Launchers
{
    /// <summary>Starts a native executable.</summary>
    public sealed class ExeLauncher : ProcessLauncherBase
    {
        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get { yield return ShortcutType.Exe; }
        }

        protected override ShortcutType PrimaryType => ShortcutType.Exe;

        public override bool CanLaunch(ShortcutItem item)
        {
            return item != null && item.Type == ShortcutType.Exe && !string.IsNullOrWhiteSpace(item.Target);
        }

        protected override bool ValidateTarget(string resolvedTarget)
        {
            // An .exe that is not there must be reported, not handed to the shell.
            return File.Exists(resolvedTarget) && !Directory.Exists(resolvedTarget);
        }

        protected override string NotFoundMessage(string target)
        {
            return "The program no longer exists: " + target;
        }
    }

    /// <summary>
    /// Starts the target of a Windows shell link.
    /// </summary>
    /// <remarks>
    /// The legacy code handed the <c>.lnk</c> itself to <c>Process.Start</c> with
    /// <c>UseShellExecute = false</c>. That works only by accident, on the subset
    /// of machines where the shell happens to be involved anyway, and it is why
    /// Office shortcuts - which are .lnk files with a document target - failed to
    /// open. This launcher resolves the link, keeps its own arguments and working
    /// directory unless the item overrides them, and then starts the real target
    /// through the shell so file associations are honoured.
    /// </remarks>
    public sealed class LnkLauncher : ProcessLauncherBase
    {
        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get
            {
                yield return ShortcutType.Lnk;
                yield return ShortcutType.AppRefMs;
            }
        }

        protected override ShortcutType PrimaryType => ShortcutType.Lnk;

        public override bool CanLaunch(ShortcutItem item)
        {
            return item != null && (item.Type == ShortcutType.Lnk || item.Type == ShortcutType.AppRefMs);
        }

        protected override string PrepareTarget(ShortcutItem item, LaunchContext context)
        {
            string linkPath = StringHelpers.ExpandPath(item.Target);

            if (!File.Exists(linkPath))
                return linkPath;

            ShellLinkInfo info = ShellLinkReader.ReadAll(linkPath);
            if (!info.IsValid || string.IsNullOrWhiteSpace(info.TargetPath))
                return linkPath;

            // A link that points at a URI is really a protocol launcher; hand the
            // URI to the shell rather than trying to create a process from it.
            if (info.TargetIsUri)
                return info.TargetPath;

            return StringHelpers.ExpandPath(info.TargetPath);
        }

        protected override bool ValidateTarget(string resolvedTarget)
        {
            if (Directory.Exists(resolvedTarget)) return true;
            if (File.Exists(resolvedTarget)) return true;

            // A protocol URI needs no file to exist.
            return Uri.TryCreate(resolvedTarget, UriKind.Absolute, out Uri? uri) && !uri.IsFile;
        }

        protected override string NotFoundMessage(string target)
        {
            if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
                return "No application is registered to handle this link.";

            return "The target of this shortcut is missing: " + target;
        }

        protected override ProcessStartInfo BuildStartInfo(ShortcutItem item, LaunchContext context, string resolvedTarget)
        {
            ProcessStartInfo startInfo;

            if (Uri.TryCreate(resolvedTarget, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
            {
                startInfo = new ProcessStartInfo { FileName = resolvedTarget, UseShellExecute = true };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = resolvedTarget,
                    // UseShellExecute must be true or file associations are
                    // bypassed, which is what breaks .lnk targets that are
                    // documents.
                    UseShellExecute = true
                };
            }

            // The item's own arguments win. When it has none, the link's own
            // arguments are inherited, which is what a user expects when they
            // drop a Steam or Office shortcut into a group.
            startInfo.Arguments = string.IsNullOrWhiteSpace(item.Arguments) && File.Exists(resolvedTarget) == false
                ? item.Arguments ?? string.Empty
                : ResolveLinkArguments(item, resolvedTarget);

            if (item.RunAsAdministrator)
                startInfo.Verb = "runas";

            if (!Directory.Exists(startInfo.WorkingDirectory ?? string.Empty))
            {
                string workingDirectory = ResolveWorkingDirectory(item, context, resolvedTarget);
                if (!string.IsNullOrEmpty(workingDirectory))
                    startInfo.WorkingDirectory = workingDirectory;
            }

            return startInfo;
        }

        private static string ResolveLinkArguments(ShortcutItem item, string resolvedTarget)
        {
            if (!string.IsNullOrWhiteSpace(item.Arguments))
                return item.Arguments!;

            string linkPath = StringHelpers.ExpandPath(item.Target);
            if (!File.Exists(linkPath)) return string.Empty;

            try
            {
                return ShellLinkReader.GetArguments(linkPath);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        protected override string ResolveWorkingDirectory(ShortcutItem item, LaunchContext context, string resolvedTarget)
        {
            if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
                return base.ResolveWorkingDirectory(item, context, resolvedTarget);

            string linkPath = StringHelpers.ExpandPath(item.Target);
            if (File.Exists(linkPath))
            {
                try
                {
                    string linkWorkingDirectory = ShellLinkReader.GetWorkingDirectory(linkPath);
                    if (!string.IsNullOrWhiteSpace(linkWorkingDirectory) && Directory.Exists(linkWorkingDirectory))
                        return linkWorkingDirectory;
                }
                catch (Exception)
                {
                }
            }

            return base.ResolveWorkingDirectory(item, context, resolvedTarget);
        }
    }

    /// <summary>Opens a folder in Explorer.</summary>
    /// <remarks>
    /// Folders were the reported P0 crash: <c>frmGroup.handleLnkExt</c> and
    /// <c>ucProgramShortcut_Load</c> both called <c>Icon.ExtractAssociatedIcon</c>
    /// on the folder path, and <c>frmMain.OpenFile</c> then tried to CreateProcess
    /// on it. Opening a directory is a shell operation, so that is what this does.
    /// </remarks>
    public sealed class FolderLauncher : ShellActivateLauncherBase
    {
        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get { yield return ShortcutType.Folder; }
        }

        protected override ShortcutType PrimaryType => ShortcutType.Folder;

        public override bool CanLaunch(ShortcutItem item)
        {
            return item != null && item.Type == ShortcutType.Folder && !string.IsNullOrWhiteSpace(item.Target);
        }

        protected override string PrepareTarget(string target)
        {
            return StringHelpers.ExpandPath(target);
        }
    }

    /// <summary>Opens a web address or any other URI through the shell.</summary>
    public sealed class UrlLauncher : ShellActivateLauncherBase
    {
        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get { yield return ShortcutType.Url; }
        }

        protected override ShortcutType PrimaryType => ShortcutType.Url;

        public override bool CanLaunch(ShortcutItem item)
        {
            return item != null && item.Type == ShortcutType.Url && !string.IsNullOrWhiteSpace(item.Target);
        }

        protected override string PrepareTarget(string target)
        {
            string trimmed = target.Trim();

            // A bare "example.com" is almost always what the user meant, so it is
            // promoted to https rather than being handed to the shell and refused.
            if (!trimmed.Contains("://", StringComparison.Ordinal) &&
                trimmed.IndexOf(':', StringComparison.Ordinal) < 0 &&
                trimmed.Contains('.', StringComparison.Ordinal))
            {
                return "https://" + trimmed;
            }

            return trimmed;
        }
    }

    /// <summary>Launches a packaged (Store) application through the shell:appsFolder namespace.</summary>
    public sealed class UwpLauncher : ShellActivateLauncherBase
    {
        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get
            {
                yield return ShortcutType.Uwp;
                yield return ShortcutType.Msix;
            }
        }

        protected override ShortcutType PrimaryType => ShortcutType.Uwp;

        public override bool CanLaunch(ShortcutItem item)
        {
            return item != null && (item.Type == ShortcutType.Uwp || item.Type == ShortcutType.Msix);
        }

        protected override string PrepareTarget(string target)
        {
            string value = target.Trim();

            const string appsFolder = "shell:appsFolder\\";
            if (value.StartsWith(appsFolder, StringComparison.OrdinalIgnoreCase))
                return value;

            if (value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                return "shell:appsFolder\\" + value.Substring("shell:".Length).Trim();

            return "shell:appsFolder\\" + value;
        }
    }

    /// <summary>Launches a Steam title through the steam:// protocol.</summary>
    public sealed class SteamLauncher : ShellActivateLauncherBase
    {
        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get { yield return ShortcutType.Steam; }
        }

        protected override ShortcutType PrimaryType => ShortcutType.Steam;

        public override bool CanLaunch(ShortcutItem item)
        {
            return item != null && item.Type == ShortcutType.Steam && !string.IsNullOrWhiteSpace(item.Target);
        }

        protected override string PrepareTarget(string target)
        {
            string value = target.Trim();
            if (value.StartsWith("steam:", StringComparison.OrdinalIgnoreCase)) return value;
            return "steam://rungameid/" + value.Trim();
        }
    }

    /// <summary>
    /// Launches a browser progressive web app.
    /// </summary>
    /// <remarks>
    /// PWAs are not registered in Windows, so there is no protocol handler to call.
    /// They are launched by starting the owning browser with
    /// <c>--app=&lt;url&gt;</c> and a profile directory, which is the documented
    /// and supported route for all three browsers this targets.
    /// </remarks>
    public sealed class PwaLauncher : ProcessLauncherBase
    {
        private readonly Func<string, string?> _browserLocator;

        public PwaLauncher(Func<string, string?> browserLocator)
        {
            _browserLocator = browserLocator ?? throw new ArgumentNullException(nameof(browserLocator));
        }

        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get { yield return ShortcutType.Pwa; }
        }

        protected override ShortcutType PrimaryType => ShortcutType.Pwa;

        public override bool CanLaunch(ShortcutItem item)
        {
            return item != null && item.Type == ShortcutType.Pwa && !string.IsNullOrWhiteSpace(item.Target);
        }

        protected override string PrepareTarget(ShortcutItem item, LaunchContext context)
        {
            string? browser = _browserLocator(item.Target);
            if (string.IsNullOrWhiteSpace(browser))
            {
                throw new InvalidOperationException(
                    "No browser that supports installed web apps was found. Install Edge, Chrome or Brave, or change this shortcut to a URL.");
            }

            return browser!;
        }

        protected override bool ValidateTarget(string resolvedTarget)
        {
            return File.Exists(resolvedTarget);
        }

        protected override string NotFoundMessage(string target)
        {
            return "The browser for this web app was not found: " + target;
        }

        protected override ProcessStartInfo BuildStartInfo(ShortcutItem item, LaunchContext context, string resolvedTarget)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedTarget,
                UseShellExecute = true
            };

            // Browser first, then --profile-directory when the item names one,
            // then --app=<url>, then the user's own arguments last so they can
            // override anything.
            var parts = new List<string>();

            string profile = ExtractProfile(item.Target);
            if (profile.Length > 0)
                parts.Add("--profile-directory=\"" + profile + "\"");

            parts.Add("--app=\"" + item.Target + "\"");

            if (!string.IsNullOrWhiteSpace(item.Arguments))
                parts.Add(item.Arguments!);

            startInfo.Arguments = string.Join(" ", parts);

            return startInfo;
        }

        /// <summary>
        /// Reads the profile out of a PWA target, which discovery encodes as a
        /// fragment: <c>https://app/|#profile=Work</c>.
        /// </summary>
        internal static string ExtractProfile(string target)
        {
            if (string.IsNullOrEmpty(target)) return string.Empty;

            int marker = target.IndexOf("profile=", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return string.Empty;

            string value = target.Substring(marker + "profile=".Length);
            int end = value.IndexOf('|');
            if (end >= 0) value = value.Substring(0, end);

            return value.Trim();
        }
    }

    /// <summary>
    /// Runs a batch file, command script or PowerShell script.
    /// </summary>
    /// <remarks>
    /// Scripts are launched through the interpreter that owns them, never through a
    /// shell command line built by string concatenation: the target is the
    /// executable and the arguments are a separate field, so a path containing
    /// <c>&amp;</c>, <c>|</c> or a quote cannot become a second command. The
    /// whole script category can be switched off in settings, and the editor warns
    /// before an item is saved.
    /// </remarks>
    public sealed class ScriptLauncher : ProcessLauncherBase
    {
        private readonly IAppLogger? _logger;

        public ScriptLauncher(IAppLogger? logger = null)
        {
            _logger = logger;
        }

        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get
            {
                yield return ShortcutType.Bat;
                yield return ShortcutType.Cmd;
                yield return ShortcutType.Ps1;
            }
        }

        protected override ShortcutType PrimaryType
        {
            get
            {
                // Reported per item; this is only used for the type carried on a
                // result when the caller did not set one.
                return ShortcutType.Cmd;
            }
        }

        public override bool CanLaunch(ShortcutItem item)
        {
            return item != null && item.Type.IsScript() && !string.IsNullOrWhiteSpace(item.Target);
        }

        public override LaunchResult Launch(ShortcutItem item, LaunchContext context)
        {
            if (item != null && item.Type == ShortcutType.Ps1 && (context == null || !context.ScriptsEnabled))
            {
                return LaunchResult.Skipped(
                    ShortcutType.Ps1,
                    item.Target ?? string.Empty,
                    LaunchErrorCode.ScriptsDisabled,
                    "Running scripts is switched off in Settings.");
            }

            return base.Launch(item!, context!);
        }

        protected override bool ValidateTarget(string resolvedTarget)
        {
            return File.Exists(resolvedTarget) && !Directory.Exists(resolvedTarget);
        }

        protected override string NotFoundMessage(string target)
        {
            return "The script no longer exists: " + target;
        }

        protected override ProcessStartInfo BuildStartInfo(ShortcutItem item, LaunchContext context, string resolvedTarget)
        {
            string interpreter = InterpreterFor(item.Type);
            string interpreterPath = LocateInterpreter(interpreter);

            var startInfo = new ProcessStartInfo
            {
                FileName = interpreterPath,
                UseShellExecute = true,
                WorkingDirectory = ResolveWorkingDirectory(item, context, resolvedTarget)
            };

            var parts = new List<string>();

            // PowerShell needs the file quoted; cmd.exe needs /c followed by a
            // quoted command. In both cases the target is a separate argument,
            // never pasted into a string the shell re-parses.
            if (item.Type == ShortcutType.Ps1)
            {
                parts.Add("-NoProfile");
                if (item.RunAsAdministrator)
                    parts.Add("-Command");
                parts.Add("\"" + resolvedTarget + "\"");
            }
            else
            {
                parts.Add("/c");
                parts.Add("\"" + resolvedTarget + "\"");
            }

            if (!string.IsNullOrWhiteSpace(item.Arguments))
                parts.Add(item.Arguments!);

            startInfo.Arguments = string.Join(" ", parts);

            if (item.RunAsAdministrator)
                startInfo.Verb = "runas";

            _logger?.Log(SystemLogLevel.Debug, "Launcher", "Running script " + resolvedTarget + " via " + interpreter);

            return startInfo;
        }

        private static string InterpreterFor(ShortcutType type)
        {
            switch (type)
            {
                case ShortcutType.Bat: return "cmd.exe";
                case ShortcutType.Cmd: return "cmd.exe";
                case ShortcutType.Ps1: return "powershell.exe";
                default: return "cmd.exe";
            }
        }

        /// <summary>
        /// Finds the interpreter. PowerShell 7 is preferred when it is installed;
        /// otherwise Windows PowerShell 5.1, which is always present.
        /// </summary>
        private static string LocateInterpreter(string interpreter)
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);

            if (string.Equals(interpreter, "cmd.exe", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(system, "cmd.exe");

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pwsh = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
            if (File.Exists(pwsh)) return pwsh;

            string system32 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
            if (!string.Equals(system32, system, StringComparison.OrdinalIgnoreCase))
            {
                string alternate = Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe");
                if (File.Exists(alternate)) return alternate;
            }

            return Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        }
    }

    /// <summary>Launches the other store-front protocol handlers.</summary>
    public sealed class ProtocolLauncher : ShellActivateLauncherBase
    {
        public ProtocolLauncher(params ShortcutType[] supported)
        {
            _supported = supported ?? Array.Empty<ShortcutType>();
        }

        private readonly ShortcutType[] _supported;

        public override IEnumerable<ShortcutType> SupportedTypes => _supported;

        protected override ShortcutType PrimaryType => _supported.Length > 0 ? _supported[0] : ShortcutType.Url;

        public override bool CanLaunch(ShortcutItem item)
        {
            return item != null && _supported.Contains(item.Type) && !string.IsNullOrWhiteSpace(item.Target);
        }

        protected override string PrepareTarget(string target)
        {
            string value = target.Trim();
            if (value.Contains("://", StringComparison.Ordinal) || value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                return value;

            return value + "://";
        }
    }
}