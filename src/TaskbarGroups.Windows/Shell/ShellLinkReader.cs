using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarGroups.Windows.Shell
{
    /// <summary>
    /// Reads the contents of a .lnk through hand-declared COM interop.
    /// </summary>
    /// <remarks>
    /// The legacy build declared these same interfaces inline in
    /// <c>main/Classes/ShellLinkHelper.cs</c> to escape the COMReference that had
    /// required tlbimp and a Visual Studio install. That code is moved here
    /// unchanged in behaviour and extended with the display-name and IDList
    /// support the icon providers need.
    ///
    /// Every method is total: an unreadable or non-shortcut file returns a
    /// falsy answer rather than throwing, because these are called from icon
    /// extraction paths that must never take the UI down.
    /// </remarks>
    public static class ShellLinkReader
    {
        private const uint SLGP_RAWPATH = 0x4;
        private const uint SLGP_UNCPRIORITY = 0x2;

        /// <summary>Target path of a shortcut, or null.</summary>
        public static string? GetTargetPath(string shortcutPath)
        {
            IShellLinkW? link = CreateLoaded(shortcutPath);
            if (link == null) return null;

            try
            {
                var buffer = new StringBuilder(32768);
                link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, SLGP_RAWPATH);
                string target = buffer.ToString();
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                Release(link);
            }
        }

        /// <summary>Arguments recorded in a shortcut, or empty.</summary>
        public static string GetArguments(string shortcutPath)
        {
            IShellLinkW? link = CreateLoaded(shortcutPath);
            if (link == null) return string.Empty;

            try
            {
                var buffer = new StringBuilder(32768);
                link.GetArguments(buffer, buffer.Capacity);
                return buffer.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
            finally
            {
                Release(link);
            }
        }

        /// <summary>Working directory recorded in a shortcut, or empty.</summary>
        public static string GetWorkingDirectory(string shortcutPath)
        {
            IShellLinkW? link = CreateLoaded(shortcutPath);
            if (link == null) return string.Empty;

            try
            {
                var buffer = new StringBuilder(32768);
                link.GetWorkingDirectory(buffer, buffer.Capacity);
                return buffer.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
            finally
            {
                Release(link);
            }
        }

        /// <summary>
        /// Icon location recorded in a shortcut, together with its index. The
        /// legacy <c>handleLnkExt</c> threw away the index, so any shortcut whose
        /// icon was the third resource in the executable came out wrong.
        /// </summary>
        public static string GetIconLocation(string shortcutPath, out int iconIndex)
        {
            iconIndex = 0;
            IShellLinkW? link = CreateLoaded(shortcutPath);
            if (link == null) return string.Empty;

            try
            {
                var buffer = new StringBuilder(32768);
                link.GetIconLocation(buffer, buffer.Capacity, out iconIndex);
                return buffer.ToString();
            }
            catch (Exception)
            {
                iconIndex = 0;
                return string.Empty;
            }
            finally
            {
                Release(link);
            }
        }

        /// <summary>True when the file exists and the shell could parse it as a link.</summary>
        public static bool IsValidShellLink(string shortcutPath)
        {
            if (string.IsNullOrWhiteSpace(shortcutPath)) return false;
            if (!File.Exists(shortcutPath)) return false;

            IShellLinkW? link = CreateLoaded(shortcutPath);
            if (link == null) return false;
            Release(link);
            return true;
        }

        /// <summary>
        /// The name Windows itself would show for a filesystem entry. For a
        /// shortcut this is the description or the target's display name, which is
        /// what made the old editor show "Google Chrome" instead of "chrome".
        /// </summary>
        public static string GetDisplayName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            try
            {
                if (Directory.Exists(path))
                    return new DirectoryInfo(path).Name;

                if (!File.Exists(path)) return System.IO.Path.GetFileName(path);

                string description = GetDescription(path);
                if (!string.IsNullOrWhiteSpace(description)) return description;

                if (string.Equals(System.IO.Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string? target = GetTargetPath(path);
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        if (Directory.Exists(target)) return new DirectoryInfo(target).Name;
                        string name = System.IO.Path.GetFileNameWithoutExtension(target);
                        if (!string.IsNullOrWhiteSpace(name)) return name;
                    }
                }

                return System.IO.Path.GetFileNameWithoutExtension(path);
            }
            catch (Exception)
            {
                return System.IO.Path.GetFileName(path);
            }
        }

        private static string GetDescription(string path)
        {
            IShellLinkW? link = CreateLoaded(path);
            if (link == null) return string.Empty;

            try
            {
                var buffer = new StringBuilder(1024);
                link.GetDescription(buffer, buffer.Capacity);
                return buffer.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
            finally
            {
                Release(link);
            }
        }

        /// <summary>
        /// Resolves a link's arguments and working directory in one COM activation
        /// rather than three. Returns the target, or null when the link is
        /// unreadable.
        /// </summary>
        public static ShellLinkInfo ReadAll(string shortcutPath)
        {
            var info = new ShellLinkInfo();
            IShellLinkW? link = CreateLoaded(shortcutPath);
            if (link == null) return info;

            try
            {
                var path = new StringBuilder(32768);
                link.GetPath(path, path.Capacity, IntPtr.Zero, SLGP_UNCPRIORITY | SLGP_RAWPATH);
                info.TargetPath = path.ToString();

                var args = new StringBuilder(32768);
                link.GetArguments(args, args.Capacity);
                info.Arguments = args.ToString();

                var wd = new StringBuilder(32768);
                link.GetWorkingDirectory(wd, wd.Capacity);
                info.WorkingDirectory = wd.ToString();

                var icon = new StringBuilder(32768);
                link.GetIconLocation(icon, icon.Capacity, out int index);
                info.IconLocation = icon.ToString();
                info.IconIndex = index;

                var desc = new StringBuilder(1024);
                link.GetDescription(desc, desc.Capacity);
                info.Description = desc.ToString();

                info.IsValid = true;
            }
            catch (Exception)
            {
                // Leave IsValid false; callers fall back.
            }
            finally
            {
                Release(link);
            }

            return info;
        }

        private static IShellLinkW? CreateLoaded(string shortcutPath)
        {
            try
            {
                var link = (IShellLinkW)new CShellLink();
                ((IPersistFile)link).Load(shortcutPath, 0);
                return link;
            }
            catch (Exception)
            {
                // Not a shortcut, unreadable, or the shell refused to load it.
                return null;
            }
        }

        private static void Release(object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
            {
                try
                {
                    Marshal.ReleaseComObject(comObject);
                }
                catch (Exception)
                {
                    // Releasing twice is harmless; nothing to recover.
                }
            }
        }

        #region COM declarations

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotKey(out short wHotKey);
            void SetHotKey(short wHotKey);
            void GetShowCmd(out uint iShowCmd);
            void SetShowCmd(uint iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int iIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IPersistFile
        {
            void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)]
        internal class CShellLink { }

        #endregion
    }

    /// <summary>Everything read out of one .lnk in a single activation.</summary>
    public sealed class ShellLinkInfo
    {
        public bool IsValid { get; set; }

        public string TargetPath { get; set; } = string.Empty;

        public string Arguments { get; set; } = string.Empty;

        public string WorkingDirectory { get; set; } = string.Empty;

        public string IconLocation { get; set; } = string.Empty;

        public int IconIndex { get; set; }

        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// True when the shortcut points at a URI rather than a path, which is how
        /// Steam and Office shortcuts are stored.
        /// </summary>
        public bool TargetIsUri => Uri.TryCreate(TargetPath, UriKind.Absolute, out Uri? uri)
                                    && !uri.IsFile;
    }
}