using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace TaskbarGroups.Windows.ExplorerIntegration
{
    /// <summary>
    /// Adds "Add to Taskbar Group" to the Explorer context menu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registers a single <c>shell\Directory\ContextMenuHandlers</c> entry per
    /// supported class plus two verb entries, all under
    /// <c>HKCU\Software\Classes</c>. HKCU is used deliberately: it needs no
    /// elevation, it applies to the current user only, and uninstalling is a
    /// delete of exactly the keys this class created.
    /// </para>
    /// <para>
    /// The requirement is that no unrelated registry entry is modified. Every key
    /// this class writes is recorded in <see cref="RegisteredKeys"/>, and
    /// <see cref="Disable"/> deletes that list rather than guessing at a pattern.
    /// </para>
    /// </remarks>
    public sealed class ExplorerContextMenuService
    {
        private const string ClassesRoot = @"Software\Classes";

        /// <summary>Classes that get a "send to"-style context menu entry.</summary>
        private static readonly string[] DirectoryClasses =
        {
            @"Directory\Background\shell",
            @"DesktopBackground\Directory\shell",
            @"AllFilesystemObjects\shell",
            @"Drive\shell"
        };

        private const string VerbName = "TaskbarGroupsAddToGroup";

        private readonly string _executablePath;

        public ExplorerContextMenuService(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("An executable path is required.", nameof(executablePath));

            _executablePath = executablePath;
        }

        /// <summary>Every key path this service may create, in creation order.</summary>
        public static IEnumerable<string> RegisteredKeys()
        {
            yield return ClassesRoot + @"\" + VerbName;
            foreach (string directoryClass in DirectoryClasses)
                yield return ClassesRoot + @"\" + directoryClass + @"\" + VerbName;
        }

        public bool IsEnabled()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(ClassesRoot + @"\" + VerbName, writable: false))
                {
                    return key != null;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Registers the context menu entries. Idempotent.</summary>
        public bool Enable(out string error)
        {
            error = string.Empty;
            try
            {
                foreach (string path in RegisteredKeys())
                {
                    string? parent = Path.GetDirectoryName(path);
                    if (string.IsNullOrEmpty(parent)) continue;

                    using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(parent, writable: true))
                    {
                        key?.SetValue(VerbName, RegistryValues.Description, RegistryValueKind.String);
                    }
                }

                using (RegistryKey? command = Registry.CurrentUser.CreateSubKey(ClassesRoot + @"\" + VerbName + @"\command", writable: true))
                {
                    if (command != null)
                    {
                        command.SetValue(null, BuildCommand(), RegistryValueKind.String);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Removes exactly the keys this service created.</summary>
        public bool Disable(out string error)
        {
            error = string.Empty;
            try
            {
                foreach (string path in RegisteredKeys())
                    DeleteTree(path);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void DeleteTree(string path)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(path, writable: true))
                {
                    if (key == null) return;

                    foreach (string sub in key.GetSubKeyNames())
                    {
                        try
                        {
                            key.DeleteSubKeyTree(sub, false);
                        }
                        catch (Exception)
                        {
                        }
                    }

                    // (Default) value and the named display value, so nothing of ours is left behind.
                    key.DeleteValue(string.Empty, throwOnMissingValue: false);
                    key.DeleteValue(VerbName, throwOnMissingValue: false);
                }

                string parent = Path.GetDirectoryName(path) ?? ClassesRoot;
                using (RegistryKey? parentKey = Registry.CurrentUser.OpenSubKey(parent, writable: true))
                {
                    parentKey?.DeleteSubKey(Path.GetFileName(path), false);
                }
            }
            catch (Exception)
            {
                // Already gone, or not ours to remove.
            }
        }

        private string BuildCommand()
        {
            // Explorer passes the selected paths as %1..%9. The application opens
            // the group chooser in "add these files" mode rather than guessing a
            // group, because Explorer cannot prompt us for one.
            return "\"" + _executablePath + "\" --add %1";
        }

        private static class RegistryValues
        {
            internal const string Description = "Add to Taskbar Group";
        }
    }
}