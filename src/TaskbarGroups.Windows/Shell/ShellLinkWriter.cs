using System;
using System.IO;
using System.Runtime.InteropServices;
using TaskbarGroups.Core.Constants;
using TaskbarGroups.Core.Models;

namespace TaskbarGroups.Windows.Shell
{
    /// <summary>
    /// Creates .lnk files, including the per-group AppUserModelID that makes a
    /// group popup appear as its own taskbar button.
    /// </summary>
    /// <remarks>
    /// This is the mechanism the whole product rests on: Windows groups taskbar
    /// buttons by AppUserModelID, so without
    /// <see cref="AppContract.GroupAppIdPrefix"/> every group popup would stack
    /// on the dashboard, and a user who already pinned a group in 1.x would find
    /// their pin pointing at nothing after an upgrade.
    ///
    /// The legacy implementation is preserved behaviourally, with two fixes: the
    /// PROPVARIANT's CoTaskMem string is now cleared instead of leaked, and the
    /// COM objects are released.
    /// </remarks>
    public static class ShellLinkWriter
    {
        /// <summary>
        /// Writes a shortcut. All parameters are validated; on failure the error
        /// is returned rather than thrown, because the caller is usually a UI
        /// handler.
        /// </summary>
        public static bool TryCreate(
            string shortcutPath,
            string targetPath,
            string? arguments,
            string? workingDirectory,
            string? description,
            string? iconLocation,
            int iconIndex,
            string? appUserModelId,
            string? hotkey,
            out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(shortcutPath))
            {
                error = "Shortcut path is required.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                error = "Shortcut target is required.";
                return false;
            }

            IShellLinkW? link = null;
            IPropertyStore? store = null;
            IPersistFile? persist = null;
            PropVariantHelper? propVariant = null;

            try
            {
                link = (IShellLinkW)new ShellLinkReader.CShellLink();

                link.SetPath(targetPath);
                link.SetArguments(arguments ?? string.Empty);
                link.SetWorkingDirectory(workingDirectory ?? string.Empty);
                link.SetDescription(description ?? string.Empty);
                link.SetShowCmd(1);

                if (!string.IsNullOrWhiteSpace(iconLocation))
                {
                    link.SetIconLocation(iconLocation, iconIndex);
                }

                if (!string.IsNullOrWhiteSpace(hotkey) &&
                    HotkeyGesture.TryParse(hotkey, out HotkeyGesture gesture))
                {
                    link.SetHotKey((short)((gesture.Modifiers << 8) | (gesture.KeyCode & 0xFF)));
                }

                if (!string.IsNullOrWhiteSpace(appUserModelId))
                {
                    store = (IPropertyStore)link;
                    propVariant = new PropVariantHelper();
                    propVariant.SetValue(appUserModelId);
                    store.SetValue(PropertyKeys.AppUserModelId, propVariant.PropVariant);
                    store.Commit();
                }

                string? directory = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                persist = (IPersistFile)link;
                persist.Save(shortcutPath, true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                propVariant?.Dispose();
                Release(ref persist);
                Release(ref link);
                Release(ref store);
            }
        }

        private static void Release<T>(ref T? comObject) where T : class
        {
            if (comObject != null && Marshal.IsComObject(comObject))
            {
                try
                {
                    Marshal.ReleaseComObject(comObject);
                }
                catch (Exception)
                {
                    // Already released or never acquired.
                }
            }
            comObject = null;
        }

        #region COM declarations

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;

            public PROPERTYKEY(Guid guid, uint id)
            {
                fmtid = guid;
                pid = id;
            }
        }

        internal static class PropertyKeys
        {
            /// <summary>
            /// System.AppUserModel.ID, the property that pins a shortcut to its
            /// own taskbar grouping.
            /// </summary>
            public static readonly PROPERTYKEY AppUserModelId =
                new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IPropertyStore
        {
            void GetCount([Out] out uint propertyCount);
            void GetAt([In] uint propertyIndex, [Out, MarshalAs(UnmanagedType.Struct)] out PROPERTYKEY key);
            void GetValue([In, MarshalAs(UnmanagedType.Struct)] ref PROPERTYKEY key, [Out, MarshalAs(UnmanagedType.Struct)] out PROPVARIANT pv);
            void SetValue([In, MarshalAs(UnmanagedType.Struct)] ref PROPERTYKEY key, [In, MarshalAs(UnmanagedType.Struct)] ref PROPVARIANT pv);
            void Commit();
        }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotKey(out short wHotKey);
            void SetHotKey(short wHotKey);
            void GetShowCmd(out uint iShowCmd);
            void SetShowCmd(uint iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int iIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IPersistFile
        {
            void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)]
        internal class CShellLink { }

        [StructLayout(LayoutKind.Explicit)]
        internal struct PROPVARIANT
        {
            [FieldOffset(0)] internal ushort vt;
            [FieldOffset(8)] internal IntPtr unionmember;
            [FieldOffset(8)] internal UInt64 forceStructToLargeEnoughSize;
        }

        /// <summary>
        /// Wraps a PROPVARIANT so the string it holds is freed. The legacy
        /// <c>PropVariantHelper</c> allocated with <c>StringToCoTaskMemUni</c> and
        /// never freed it, leaking the string on every group save.
        /// </summary>
        internal sealed class PropVariantHelper : IDisposable
        {
            private PROPVARIANT _variant;
            private bool _ownsString;

            internal PROPVARIANT PropVariant => _variant;

            internal void SetValue(string value)
            {
                Clear();
                _variant.vt = (ushort)VarEnum.VT_LPWSTR;
                _variant.unionmember = Marshal.StringToCoTaskMemUni(value ?? string.Empty);
                _ownsString = true;
            }

            internal void Clear()
            {
                if (_ownsString && _variant.unionmember != IntPtr.Zero)
                {
                    try
                    {
                        NativeMethods.PropVariantClear(ref _variant);
                    }
                    catch (Exception)
                    {
                        // Nothing to do; the allocation is about to be forgotten.
                    }
                }
                _variant = default;
                _ownsString = false;
            }

            public void Dispose() => Clear();
        }

        private static class NativeMethods
        {
            [DllImport("Ole32.dll", PreserveSig = false)]
            internal static extern void PropVariantClear(ref PROPVARIANT pvar);
        }

        #endregion
    }
}