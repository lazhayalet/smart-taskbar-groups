using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarGroups.Windows.Interop
{
    /// <summary>
    /// Win32 and shell declarations used across the platform layer.
    /// </summary>
    /// <remarks>
    /// Everything here is declared by hand rather than through a COMReference or
    /// a Windows SDK import, so the solution builds with nothing but the .NET SDK
    /// installed. That was already true of the legacy build (see
    /// <c>main/Classes/ShellLinkHelper.cs</c>); this is the same technique moved
    /// into a proper platform project.
    /// </remarks>
    internal static class NativeMethods
    {
        internal const int MONITORINFOF_PRIMARY = 0x00000001;

        internal const uint SHGFI_ICON = 0x000000100;
        internal const uint SHGFI_SMALLICON = 0x000000001;
        internal const uint SHGFI_LARGEICON = 0x000000000;
        internal const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

        internal const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        internal const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        internal const int SW_SHOWNORMAL = 1;
        internal const int SW_SHOWMINIMIZED = 2;
        internal const int SW_SHOWMAXIMIZED = 3;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;

            internal int Width => Right - Left;

            internal int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MONITORINFOEX
        {
            internal int cbSize;
            internal RECT rcMonitor;
            internal RECT rcWork;
            internal uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            internal string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct SHFILEINFO
        {
            internal IntPtr hIcon;
            internal int iIcon;
            internal uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            internal string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            internal string szTypeName;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DPIINFO
        {
            internal int dpiX;
            internal int dpiY;
        }

        internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("shcore.dll")]
        internal static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int nIndex);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA lParam);

        internal const uint ABM_GETSTATE = 0x00000004;
        internal const uint ABM_GETAUTOHIDEBAR = 0x0000000B;
        internal const uint ABS_AUTOHIDE = 0x0000001;
        internal const int ABE_TOP = 0;
        internal const int ABE_BOTTOM = 1;
        internal const int ABE_LEFT = 2;
        internal const int ABE_RIGHT = 3;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct APPBARDATA
        {
            internal uint cbSize;
            internal IntPtr hWnd;
            internal uint uCallbackMessage;
            internal uint uEdge;
            internal RECT rc;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            internal string szClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            internal int X;
            internal int Y;
        }

        internal const uint MONITOR_DEFAULTTONEAREST = 2;

        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_NOOWNERZORDER = 0x0200;
        internal const uint SWP_FRAMECHANGED = 0x0020;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOSIZE = 0x0001;

        /// <summary>
        /// True when the taskbar is currently set to auto-hide. Read from the
        /// shell's appbar state rather than guessed from the working area, because
        /// a monitor with an auto-hidden taskbar reports no reserved strip at all
        /// and the legacy code treated that as "no taskbar anywhere".
        /// </summary>
        internal static bool IsAutoHideEnabled()
        {
            APPBARDATA data = default;
            data.cbSize = (uint)Marshal.SizeOf<APPBARDATA>();
            IntPtr result = SHAppBarMessage(ABM_GETAUTOHIDEBAR, ref data);
            return result != IntPtr.Zero && (data.rc.Width != 0 || data.rc.Height != 0 || IsAutoHideEx());
        }

        private static bool IsAutoHideEx()
        {
            try
            {
                APPBARDATA state = default;
                state.cbSize = (uint)Marshal.SizeOf<APPBARDATA>();
                IntPtr value = SHAppBarMessage(ABM_GETSTATE, ref state);
                return ((uint)value.ToInt64() & ABS_AUTOHIDE) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}