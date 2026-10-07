using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;

namespace TaskbarGroups.Launchers.Abstractions
{
    /// <summary>
    /// Shared process-starting behaviour for the launchers that create a process.
    /// </summary>
    /// <remarks>
    /// Two rules are enforced here rather than in each launcher:
    /// <list type="bullet">
    /// <item><c>UseShellExecute = true</c> whenever anything other than the
    /// executable itself is being relied on - file associations, URI protocols,
    /// elevation. The legacy code left this false, which is why Office documents,
    /// folders and <c>.url</c> files failed to open.</item>
    /// <item>Arguments and the working directory are validated independently. The
    /// legacy editor forced the working directory back to the target's folder when
    /// it was invalid, silently discarding what the user had typed.</item>
    /// </list>
    /// </remarks>
    public abstract class ProcessLauncherBase : IApplicationLauncher
    {
        public abstract IEnumerable<ShortcutType> SupportedTypes { get; }

        public abstract bool CanLaunch(ShortcutItem item);

        public virtual LaunchResult Launch(ShortcutItem item, LaunchContext context)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            context ??= new LaunchContext();

            string target = item.Target ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target))
                return LaunchResult.Fail(PrimaryType, target, LaunchErrorCode.EmptyTarget, "This shortcut has no target.");

            if (!item.Enabled)
                return LaunchResult.Skipped(PrimaryType, target, LaunchErrorCode.Disabled, "This shortcut is disabled.");

            string resolvedTarget;
            try
            {
                resolvedTarget = PrepareTarget(item, context);
            }
            catch (Exception ex)
            {
                return LaunchResult.Fail(PrimaryType, target, LaunchErrorCode.TargetNotFound, ex.Message);
            }

            if (string.IsNullOrWhiteSpace(resolvedTarget))
                return LaunchResult.Fail(PrimaryType, target, LaunchErrorCode.TargetNotFound, "The target could not be resolved.");

            if (!ValidateTarget(resolvedTarget))
                return LaunchResult.Fail(PrimaryType, resolvedTarget, LaunchErrorCode.TargetNotFound, NotFoundMessage(resolvedTarget));

            var startInfo = BuildStartInfo(item, context, resolvedTarget);

            try
            {
                Process? process = Process.Start(startInfo);
                return LaunchResult.Ok(PrimaryType, resolvedTarget, SafeId(process));
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // Native error 1223 is "the operation was cancelled by the user",
                // which is what a declined UAC prompt looks like.
                int code = ex.NativeErrorCode;
                LaunchErrorCode errorCode = code == 1223
                    ? LaunchErrorCode.PermissionDenied
                    : code == 2
                        ? LaunchErrorCode.TargetNotFound
                        : code == 1155
                            ? LaunchErrorCode.NoAssociatedHandler
                            : LaunchErrorCode.Exception;

                return LaunchResult.Fail(PrimaryType, resolvedTarget, errorCode, DescribeWin32(code, ex.Message));
            }
            catch (FileNotFoundException)
            {
                return LaunchResult.Fail(PrimaryType, resolvedTarget, LaunchErrorCode.TargetNotFound, NotFoundMessage(resolvedTarget));
            }
            catch (DirectoryNotFoundException)
            {
                return LaunchResult.Fail(PrimaryType, resolvedTarget, LaunchErrorCode.TargetNotFound, NotFoundMessage(resolvedTarget));
            }
            catch (Exception ex)
            {
                return LaunchResult.Fail(PrimaryType, resolvedTarget, LaunchErrorCode.Exception, ex.Message);
            }
        }

        /// <summary>The type this launcher primarily reports on results.</summary>
        protected abstract ShortcutType PrimaryType { get; }

        /// <summary>
        /// Converts the stored target into something startable. The default
        /// expands environment variables and normalises the path.
        /// </summary>
        protected virtual string PrepareTarget(ShortcutItem item, LaunchContext context)
        {
            return Core.Validation.StringHelpers.ExpandPath(item.Target);
        }

        /// <summary>Decides whether the resolved target may be started.</summary>
        protected abstract bool ValidateTarget(string resolvedTarget);

        protected virtual string NotFoundMessage(string target)
        {
            return "Target not found: " + target;
        }

        protected virtual ProcessStartInfo BuildStartInfo(ShortcutItem item, LaunchContext context, string resolvedTarget)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedTarget,
                Arguments = item.Arguments ?? string.Empty,
                WorkingDirectory = ResolveWorkingDirectory(item, context, resolvedTarget),
                UseShellExecute = true,
                CreateNoWindow = false
            };

            if (item.RunAsAdministrator)
            {
                // ShellExecute is what makes the "runas" verb work. The user gets
                // the standard UAC prompt; nothing is elevated silently, and this
                // only ever happens because the user ticked the box on the item.
                startInfo.Verb = "runas";
            }

            return startInfo;
        }

        /// <summary>
        /// Picks the working directory. An explicitly configured one wins even if
        /// it does not currently exist - the app may create it - but a target that
        /// does not exist falls back to the default rather than throwing.
        /// </summary>
        protected virtual string ResolveWorkingDirectory(ShortcutItem item, LaunchContext context, string resolvedTarget)
        {
            string configured = (item.WorkingDirectory ?? string.Empty).Trim();

            if (configured.Length > 0)
            {
                try
                {
                    string expanded = Core.Validation.StringHelpers.ExpandPath(configured);
                    if (Directory.Exists(expanded)) return expanded;
                }
                catch (Exception)
                {
                }
            }

            try
            {
                string? parent = Path.GetDirectoryName(resolvedTarget);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) return parent!;
            }
            catch (Exception)
            {
            }

            return context.DefaultWorkingDirectory;
        }

        private static string DescribeWin32(int nativeError, string fallback)
        {
            switch (nativeError)
            {
                case 1223: return "Elevation was declined or cancelled.";
                case 2: return "The file was not found.";
                case 3: return "The path was not found.";
                case 5: return "Access was denied.";
                case 1155: return "No application is registered to open this file type.";
                default: return string.IsNullOrWhiteSpace(fallback) ? "Windows error " + nativeError : fallback;
            }
        }

        private static int? SafeId(Process? process)
        {
            if (process == null) return null;
            try
            {
                return process.Id;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Launchers that do not create a process but hand a URI to the shell.</summary>
    public abstract class ShellActivateLauncherBase : IApplicationLauncher
    {
        public abstract IEnumerable<ShortcutType> SupportedTypes { get; }

        public abstract bool CanLaunch(ShortcutItem item);

        public LaunchResult Launch(ShortcutItem item, LaunchContext context)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            string target = (item.Target ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(target))
                return LaunchResult.Fail(PrimaryType, target, LaunchErrorCode.EmptyTarget, "This shortcut has no target.");

            if (!item.Enabled)
                return LaunchResult.Skipped(PrimaryType, target, LaunchErrorCode.Disabled, "This shortcut is disabled.");

            string resolved;
            try
            {
                resolved = PrepareTarget(target);
            }
            catch (Exception ex)
            {
                return LaunchResult.Fail(PrimaryType, target, LaunchErrorCode.Exception, ex.Message);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = resolved,
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(item.Arguments))
                startInfo.Arguments = item.Arguments!;

            if (!string.IsNullOrWhiteSpace(item.WorkingDirectory) && Directory.Exists(item.WorkingDirectory))
                startInfo.WorkingDirectory = item.WorkingDirectory!;

            try
            {
                Process? process = Process.Start(startInfo);
                return LaunchResult.Ok(PrimaryType, resolved, process == null ? null : (int?)process.Id);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                LaunchErrorCode code = ex.NativeErrorCode switch
                {
                    1155 => LaunchErrorCode.NoAssociatedHandler,
                    2 or 3 => LaunchErrorCode.TargetNotFound,
                    5 => LaunchErrorCode.PermissionDenied,
                    _ => LaunchErrorCode.Exception
                };

                string message = ex.NativeErrorCode switch
                {
                    1155 => "No application is registered to handle " + ShortScheme(resolved) + " links.",
                    2 => "The target was not found: " + resolved,
                    _ => ex.Message
                };

                return LaunchResult.Fail(PrimaryType, resolved, code, message);
            }
            catch (Exception ex)
            {
                return LaunchResult.Fail(PrimaryType, resolved, LaunchErrorCode.Exception, ex.Message);
            }
        }

        protected abstract ShortcutType PrimaryType { get; }

        protected virtual string PrepareTarget(string target) => target;

        private static string ShortScheme(string uri)
        {
            int colon = uri.IndexOf(':');
            return colon > 0 ? uri.Substring(0, colon) : "this";
        }
    }
}