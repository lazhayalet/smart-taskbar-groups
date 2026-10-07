using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Windows.Dpi;
using TaskbarGroups.Windows.Monitor;
using TaskbarGroups.Windows.Shell;

namespace TaskbarGroups.Windows.Taskbar
{
    /// <summary>
    /// Finds the taskbar and maintains the shortcuts that put group popups on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows 11 does not expose a supported way to pin a shortcut to the taskbar
    /// from a running process. The shell's internal verb is undocumented, was
    /// restricted in Windows 11, and is explicitly blocked for non-packaged
    /// desktop apps. This class therefore does the two things that are both
    /// reliable and honest: it creates and repairs the .lnk that the user can pin
    /// (or that the installer pins for them), and it reports whether that file is
    /// still intact. Any UI copy about pinning says so plainly rather than
    /// implying an automation that does not exist.
    /// </para>
    /// <para>
    /// The AppUserModelID scheme and the argument contract are fixed by
    /// <see cref="Core.Constants.AppContract"/> and must not drift.
    /// </para>
    /// </remarks>
    public sealed class TaskbarService : ITaskbarService
    {
        private readonly MonitorService _monitors;
        private readonly string _shortcutsDirectory;
        private readonly string _executablePath;

        public TaskbarService(MonitorService monitors, string shortcutsDirectory, string executablePath)
        {
            _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));

            if (string.IsNullOrWhiteSpace(shortcutsDirectory))
                throw new ArgumentException("A shortcuts directory is required.", nameof(shortcutsDirectory));
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("An executable path is required.", nameof(executablePath));

            _shortcutsDirectory = shortcutsDirectory;
            _executablePath = executablePath;
        }

        public bool IsTaskbarPresent
        {
            get
            {
                try
                {
                    return FindShellTaskbarWindow() != IntPtr.Zero;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public Rectangle? GetTaskbarRectangle(Point physicalPoint)
        {
            foreach (MonitorInfo monitor in _monitors.GetMonitors().Monitors)
            {
                if (!monitor.TaskbarDetected) continue;
                if (!monitor.Bounds.Contains(physicalPoint)) continue;

                Rectangle work = monitor.WorkingArea;
                Rectangle bounds = monitor.Bounds;
                return Rectangle.Intersect(new Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height), work);
            }
            return null;
        }

        public bool IsOnTaskbar(Point physicalPoint)
        {
            return MonitorService.IsOnDockedTaskbar(physicalPoint);
        }

        /// <summary>
        /// Locates a group's .lnk. Both the 2.0 name (name with underscores) and
        /// the 1.x name (name with underscores turned into spaces) are checked,
        /// because the legacy writer used the spaced form while the dashboard
        /// looked for the underscored one - which is why clicking a group tile
        /// could open Explorer on nothing.
        /// </summary>
        public string? FindGroupShortcut(string groupName, bool legacyUnderscoreName)
        {
            if (string.IsNullOrWhiteSpace(groupName)) return null;

            foreach (string candidate in CandidateNames(groupName, legacyUnderscoreName))
            {
                string path = Path.Combine(_shortcutsDirectory, candidate + ".lnk");
                if (File.Exists(path)) return path;
            }
            return null;
        }

        public bool CreateGroupShortcut(string groupName, string exePath, string iconPath, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(groupName))
            {
                error = "A group name is required.";
                return false;
            }

            try
            {
                Directory.CreateDirectory(_shortcutsDirectory);

                string appId = Core.Constants.AppContract.BuildGroupAppId(groupName);

                // The argument is the group storage name, which is what the popup
                // entry point parses. Keeping it identical to 1.x is what makes
                // existing pinned shortcuts keep working after the upgrade.
                string storageName = GroupNameValidator.ToStorageName(groupName);

                string shortcutPath = Path.Combine(_shortcutsDirectory, storageName + ".lnk");

                bool ok = ShellLinkWriter.TryCreate(
                    shortcutPath,
                    string.IsNullOrWhiteSpace(exePath) ? _executablePath : exePath,
                    storageName,
                    Path.GetDirectoryName(_executablePath) ?? _shortcutsDirectory,
                    groupName + " taskbar group",
                    string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath) ? null : iconPath,
                    0,
                    appId,
                    null,
                    out error);

                return ok;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool DeleteGroupShortcut(string groupName, bool legacyUnderscoreName, out string error)
        {
            error = string.Empty;

            try
            {
                string? existing = FindGroupShortcut(groupName, legacyUnderscoreName);
                if (existing == null) return true;

                File.Delete(existing);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private IEnumerable<string> CandidateNames(string groupName, bool legacyUnderscoreName)
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (legacyUnderscoreName)
            {
                string storage = GroupNameValidator.ToStorageName(groupName);
                if (seen.Add(storage)) yield return storage;
            }

            string spaced = GroupNameValidator.ToDisplayName(groupName);
            if (seen.Add(spaced)) yield return spaced;

            if (seen.Add(groupName)) yield return groupName;
        }

        private static IntPtr FindShellTaskbarWindow()
        {
            IntPtr found = IntPtr.Zero;

            NativeMethodsSafe.EnumWindows(
                delegate (IntPtr hWnd, IntPtr _)
                {
                    if (!NativeMethodsSafe.IsWindowVisible(hWnd)) return true;

                    string className = NativeMethodsSafe.GetClassName(hWnd);
                    if (className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd" ||
                        className == "NotifyIconOverflowWindow" || className == "TopLevelWindowForOverflowXamlIsland")
                    {
                        found = hWnd;
                        return false;
                    }

                    return true;
                },
                IntPtr.Zero);

            return found;
        }
    }

    /// <summary>
    /// Small facade so <see cref="TaskbarService"/> does not have to make the
    /// interop types public.
    /// </summary>
    internal static class NativeMethodsSafe
    {
        internal static void EnumWindows(Interop.NativeMethods.EnumWindowsProc callback, IntPtr lParam)
        {
            Interop.NativeMethods.EnumWindows(callback, lParam);
        }

        internal static bool IsWindowVisible(IntPtr hWnd)
        {
            return Interop.NativeMethods.IsWindowVisible(hWnd);
        }

        internal static string GetClassName(IntPtr hWnd)
        {
            var buffer = new System.Text.StringBuilder(256);
            Interop.NativeMethods.GetClassNameW(hWnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }
    }
}