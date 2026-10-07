using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaskbarGroups.Windows.Startup
{
    /// <summary>
    /// Registers the application to start with Windows.
    /// </summary>
    /// <remarks>
    /// Uses the documented per-user Run key under HKCU, which needs no elevation,
    /// is removed cleanly on uninstall, and is visible to the user in Task Manager
    /// so they can disable it themselves. A scheduled task or a service would be
    /// heavier and would need administrator rights for no benefit in a tray app.
    ///
    /// Everything is confined to one value under <c>Software\Microsoft\Windows\CurrentVersion\Run</c>.
    /// No other registry key is touched, so enabling or disabling this setting
    /// cannot affect anything else on the machine.
    /// </remarks>
    public sealed class StartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "TaskbarGroups";

        private readonly string _executablePath;
        private readonly string[] _extraArguments;

        public StartupService(string executablePath, params string[] extraArguments)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("An executable path is required.", nameof(executablePath));

            _executablePath = executablePath;
            _extraArguments = extraArguments ?? Array.Empty<string>();
        }

        /// <summary>Whether the Run entry currently points at this executable.</summary>
        public bool IsEnabled()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false))
                {
                    if (key == null) return false;

                    object? value = key.GetValue(ValueName);
                    return value is string command && command.Contains(_executablePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Adds the Run entry. Idempotent.</summary>
        public bool Enable(out string error)
        {
            error = string.Empty;
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true))
                {
                    if (key == null)
                    {
                        error = "The Run registry key could not be opened.";
                        return false;
                    }

                    key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Removes the Run entry. Safe when it is not there.</summary>
        public bool Disable(out string error)
        {
            error = string.Empty;
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
                {
                    key?.DeleteValue(ValueName, throwOnMissingValue: false);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private string BuildCommand()
        {
            string command = "\"" + _executablePath + "\"";
            foreach (string argument in _extraArguments)
            {
                if (string.IsNullOrWhiteSpace(argument)) continue;
                command += " \"" + argument + "\"";
            }
            return command;
        }

        /// <summary>
        /// Removes the Run entry only if it points at this executable. Used by the
        /// uninstaller so a clean uninstall does not leave a broken entry behind,
        /// while an update that changed the install path does not delete a valid
        /// entry for the new location.
        /// </summary>
        public static bool RemoveIfOurs(string expectedPathFragment, out string error)
        {
            error = string.Empty;
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
                {
                    if (key == null) return true;

                    object? value = key.GetValue(ValueName);
                    if (value is string command &&
                        !string.IsNullOrEmpty(expectedPathFragment) &&
                        command.IndexOf(expectedPathFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        key.DeleteValue(ValueName, throwOnMissingValue: false);
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

        /// <summary>The user-writable Startup folder, exposed for the shortcut fallback.</summary>
        public static string StartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    }
}