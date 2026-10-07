using System;
using System.IO;
using System.Reflection;
using TaskbarGroups.Core.Constants;
using TaskbarGroups.Core.Models;

namespace TaskbarGroups.App.Configuration
{
    /// <summary>
    /// Decides where the application keeps its data.
    /// </summary>
    /// <remarks>
    /// The legacy build wrote <c>config\</c>, <c>Shortcuts\</c> and <c>JITComp\</c>
    /// relative to the process working directory. That breaks as soon as the exe is
    /// launched from a shortcut whose "Start in" points somewhere else, and it
    /// cannot work at all under Program Files. The rules here are explicit:
    /// <list type="bullet">
    /// <item>Portable mode: a <c>Data</c> folder beside the executable, selected by
    /// a <c>portable.txt</c> marker so it survives an upgrade.</item>
    /// <item>Portable mode, upgraded: an existing legacy <c>config</c> folder
    /// beside the executable is taken as the data directory, because that is where
    /// a 1.x user's data already lives.</item>
    /// <item>Otherwise: <c>%LOCALAPPDATA%\TaskbarGroups</c>, which is per-user,
    /// writable without elevation, and not roaming.</item>
    /// </list>
    /// No developer-specific path is involved anywhere.
    /// </remarks>
    public static class DataPathResolver
    {
        public const string PortableMarkerFileName = "portable.txt";
        public const string PortableFolderName = "Data";

        /// <summary>Resolves the data directory and reports which mode produced it.</summary>
        public static DataPaths Resolve(out PortableMode mode)
        {
            string? executableDirectory = TryGetExecutableDirectory();

            if (executableDirectory != null)
            {
                string marker = Path.Combine(executableDirectory, PortableMarkerFileName);
                if (File.Exists(marker))
                {
                    mode = PortableMode.Marker;
                    return new DataPaths(Path.Combine(executableDirectory, PortableFolderName));
                }

                // A portable 1.x install has its data beside the exe with no
                // marker. Keep using that location so those users find their
                // groups, the Shortcuts folder they pinned from, and their icon
                // caches where they already are.
                string legacyBeside = Path.Combine(executableDirectory, AppContract.LegacyConfigFolderName);
                if (Directory.Exists(legacyBeside) &&
                    Directory.GetFileSystemEntries(legacyBeside).Length > 0)
                {
                    mode = PortableMode.LegacyBesideExecutable;
                    return new DataPaths(executableDirectory);
                }

                if (Directory.Exists(Path.Combine(executableDirectory, AppContract.LegacyShortcutsFolderName)))
                {
                    mode = PortableMode.LegacyBesideExecutable;
                    return new DataPaths(executableDirectory);
                }
            }

            mode = PortableMode.LocalApplicationData;
            return new DataPaths(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaskbarGroups"));
        }

        /// <summary>Converts an installed layout into a portable one.</summary>
        /// <returns>True when the conversion succeeded.</returns>
        public static bool ConvertToPortable(DataPaths source, string targetDirectory, out string error)
        {
            error = string.Empty;

            try
            {
                Directory.CreateDirectory(targetDirectory);

                foreach (string name in new[]
                         {
                             "TaskbarGroups.db", "Icons", "Backups", "Logs",
                             AppContract.LegacyConfigFolderName,
                             AppContract.LegacyShortcutsFolderName
                         })
                {
                    string from = Path.Combine(source.BaseDirectory, name);
                    string to = Path.Combine(targetDirectory, name);

                    if (!Directory.Exists(from) && !File.Exists(from)) continue;

                    CopyRecursively(from, to);
                }

                File.WriteAllText(Path.Combine(targetDirectory, PortableMarkerFileName),
                    "Taskbar Groups portable mode.\r\nDelete this file and the Data folder to return to installed mode.\r\n");

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void CopyRecursively(string from, string to)
        {
            if (Directory.Exists(from))
            {
                Directory.CreateDirectory(to);

                foreach (string file in Directory.GetFiles(from))
                    File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);

                foreach (string directory in Directory.GetDirectories(from))
                    CopyRecursively(directory, Path.Combine(to, Path.GetFileName(directory)));
            }
            else if (File.Exists(from))
            {
                string? directory = Path.GetDirectoryName(to);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory!);
                File.Copy(from, to, overwrite: true);
            }
        }

        private static string? TryGetExecutableDirectory()
        {
            try
            {
                string? path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path))
                {
                    string? directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory)) return directory;
                }
            }
            catch (Exception)
            {
            }

            try
            {
                // AppContext.BaseDirectory is the supported equivalent and is
                // correct for single-file deployments too.
                return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>How the data directory was chosen.</summary>
    public enum PortableMode
    {
        /// <summary>Installed mode, using the per-user application data folder.</summary>
        LocalApplicationData = 0,

        /// <summary>Portable mode, selected by a marker file.</summary>
        Marker = 1,

        /// <summary>A 1.x portable install whose data sits beside the executable.</summary>
        LegacyBesideExecutable = 2
    }
}