using System;
using System.Drawing;
using System.Runtime.InteropServices;
using TaskbarGroups.Windows.Interop;

namespace TaskbarGroups.Windows.Dpi
{
    /// <summary>
    /// DPI awareness state of the current process, and conversions between the
    /// device-independent pixels the UI works in and the physical pixels Win32
    /// wants.
    /// </summary>
    /// <remarks>
    /// The manifest already declares PerMonitorV2, so the UI is scaled by Windows
    /// and this class does not scale anything twice. Its job is the opposite: to
    /// give callers the one number they cannot get from the framework - the scale
    /// of the monitor a particular window or point is on, which changes as the
    /// pointer crosses screens.
    /// </remarks>
    public static class DpiService
    {
        private const int MDT_EFFECTIVE_DPI = 0;

        /// <summary>The DPI Windows assumes when nothing better is known.</summary>
        public const double DefaultScale = 1.0;

        public static int DpiToPixels(int dip, double scale)
        {
            if (scale <= 0) scale = DefaultScale;
            return (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);
        }

        public static int PixelsToDip(int pixels, double scale)
        {
            if (scale <= 0) scale = DefaultScale;
            return (int)Math.Round(pixels / scale, MidpointRounding.AwayFromZero);
        }

        public static Size DipToPixels(Size dip, double scale)
        {
            return new Size(DpiToPixels(dip.Width, scale), DpiToPixels(dip.Height, scale));
        }

        public static Size PixelsToDip(Size pixels, double scale)
        {
            return new Size(PixelsToDip(pixels.Width, scale), PixelsToDip(pixels.Height, scale));
        }

        public static Point DipToPixels(Point dip, double scale)
        {
            return new Point(DpiToPixels(dip.X, scale), DpiToPixels(dip.Y, scale));
        }

        /// <summary>Scale factor of the monitor containing a physical point.</summary>
        public static double ScaleAtPoint(Point physicalPoint)
        {
            IntPtr monitor = MonitorFromPointSafe(physicalPoint);
            return monitor == IntPtr.Zero ? DefaultScale : ScaleOfMonitor(monitor);
        }

        /// <summary>
        /// Scale factor of the monitor a window is on, by window handle.
        /// </summary>
        public static double ScaleAtWindow(IntPtr window)
        {
            return ScaleOfWindow(window);
        }

        /// <summary>Scale factor of the monitor a window is on.</summary>
        public static double ScaleOfWindow(IntPtr window)
        {
            if (window == IntPtr.Zero) return DefaultScale;
            try
            {
                IntPtr monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MONITOR_DEFAULTTONEAREST);
                return monitor == IntPtr.Zero ? DefaultScale : ScaleOfMonitor(monitor);
            }
            catch (Exception)
            {
                return DefaultScale;
            }
        }

        internal static double ScaleOfMonitor(IntPtr monitor)
        {
            try
            {
                int result = NativeMethods.GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _);
                if (result == 0 && dpiX > 0)
                    return dpiX / 96d;
            }
            catch (DllNotFoundException)
            {
                // Pre-8.1 Windows; the supported floor is Windows 10 so this only
                // happens on an unexpected host.
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (Exception)
            {
            }

            return DefaultScale;
        }

        internal static IntPtr MonitorFromPointSafe(Point physicalPoint)
        {
            try
            {
                NativeMethods.POINT pt = new NativeMethods.POINT { X = physicalPoint.X, Y = physicalPoint.Y };
                return NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// The process's DPI awareness context. Surfaced on the diagnostics page
        /// because a blurry popup is almost always a process that ended up
        /// DPI-unaware, which happens when the manifest is dropped.
        /// </summary>
        public static string DescribeAwareness()
        {
            try
            {
                IntPtr context = GetThreadDpiAwarenessContext();
                if (context == IntPtr.Zero) return "Unknown";

                int awareness = GetAwarenessFromDpiAwarenessContext(context);
                switch (awareness)
                {
                    case 0: return "DPI unaware";
                    case 1: return "System DPI aware";
                    case 2: return "Per monitor DPI aware";
                    default: return "Per monitor V2 aware";
                }
            }
            catch (Exception)
            {
                return "Unknown";
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr dpiContext);
    }
}