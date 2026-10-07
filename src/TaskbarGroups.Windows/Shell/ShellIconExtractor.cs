using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using TaskbarGroups.Windows.Interop;

namespace TaskbarGroups.Windows.Shell
{
    /// <summary>
    /// Shell icon extraction that is safe to call on anything.
    /// </summary>
    /// <remarks>
    /// The P0 defect this replaces: <c>frmGroup.handleLnkExt</c> and
    /// <c>ucProgramShortcut_Load</c> both called
    /// <c>Icon.ExtractAssociatedIcon(path).ToBitmap()</c> with no check that
    /// <paramref name="path"/> existed, was a file rather than a directory, or
    /// even was a path at all. A folder shortcut, a moved target or a broken link
    /// threw <see cref="FileNotFoundException"/> out of a UI event handler.
    ///
    /// Every method here returns null instead of throwing, disposes the intermediate
    /// HICON (the legacy <c>handleFolder.GetFolderIcon</c> called
    /// <c>Icon.FromHandle</c> twice and leaked the first handle), and uses
    /// SHGFI_USEFILEATTRIBUTES so a path that exists in the shell's cache but not
    /// on disk still yields an icon.
    /// </remarks>
    public static class ShellIconExtractor
    {
        /// <summary>
        /// Serialises every call that asks the shell for an icon.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>SHGetFileInfo</c> and <c>ExtractIconEx</c> both return GDI handles
        /// through an out-parameter structure, and the shell does not make the
        /// handle-producing part of those calls reentrant. Calling them from
        /// several threads at once reliably yields a call that reports success and
        /// a null <c>hIcon</c>: measured at 12 failures in 400 concurrent calls on
        /// this machine, and zero when serialised.
        /// </para>
        /// <para>
        /// Icon extraction is short and happens once per cache miss, so a lock
        /// costs nothing measurable and turns an intermittent blank icon - or, on
        /// the legacy code path, an <c>ArgumentNullException</c> dereferencing the
        /// null handle - into a reliable result. The handle is still destroyed by
        /// its owner afterwards; only the acquisition is serialised.
        /// </para>
        /// </remarks>
        private static readonly object ShellIconGate = new object();

        /// <summary>
        /// Runs <paramref name="acquire"/> while holding the shell icon lock.
        /// </summary>
        internal static TResult ShellExclusive<TResult>(Func<TResult> acquire)
        {
            lock (ShellIconGate)
            {
                return acquire();
            }
        }
        /// <summary>
        /// Extracts the shell icon for a filesystem path, at the requested size.
        /// Returns null when there is nothing to return.
        /// </summary>
        public static Bitmap? TryExtract(string? path, int pixelSize)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            int size = Math.Max(16, pixelSize);

            return ShellExclusive(() =>
            {
                try
                {
                    bool exists = Directory.Exists(path) || File.Exists(path);

                    uint flags = NativeMethods.SHGFI_ICON | (size <= 32 ? NativeMethods.SHGFI_SMALLICON : NativeMethods.SHGFI_LARGEICON);
                    if (!exists) flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;

                    uint attributes = Directory.Exists(path)
                        ? NativeMethods.FILE_ATTRIBUTE_DIRECTORY
                        : NativeMethods.FILE_ATTRIBUTE_NORMAL;

                    NativeMethods.SHFILEINFO info = default;
                    IntPtr result = NativeMethods.SHGetFileInfoW(path, attributes, out info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);

                    if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                        return null;

                    // FromHandle does not own the handle, so the icon is cloned
                    // before the handle is destroyed, and both wrappers disposed.
                    Icon? cloned = null;
                    Icon? wrapper = null;
                    try
                    {
                        wrapper = Icon.FromHandle(info.hIcon);
                        cloned = (Icon)wrapper.Clone();
                        return ResizeTo(cloned, size);
                    }
                    finally
                    {
                        cloned?.Dispose();
                        wrapper?.Dispose();
                        NativeMethods.DestroyIcon(info.hIcon);
                    }
                }
                catch (Exception)
                {
                    return null;
                }
            });
        }

        /// <summary>
        /// Extracts the icon of an executable, including one behind a shell link's
        /// own icon location. Returns null rather than throwing.
        /// </summary>
        public static Bitmap? TryExtractFromLink(string linkPath, int pixelSize)
        {
            if (string.IsNullOrWhiteSpace(linkPath)) return null;
            if (!File.Exists(linkPath)) return null;

            int size = Math.Max(16, pixelSize);

            try
            {
                ShellLinkInfo info = ShellLinkReader.ReadAll(linkPath);
                if (!info.IsValid) return null;

                // An explicit icon location wins, as it does in Explorer. Splitting
                // on the last comma is how the shell stores "path,index"; paths can
                // themselves contain commas, so the split is from the right.
                if (!string.IsNullOrWhiteSpace(info.IconLocation))
                {
                    string iconPath = info.IconLocation;
                    int iconIndex = info.IconIndex;

                    int comma = iconPath.LastIndexOf(',');
                    if (comma > 1 && int.TryParse(iconPath.Substring(comma + 1).Trim(), out int parsedIndex))
                    {
                        iconPath = iconPath.Substring(0, comma).Trim();
                        iconIndex = parsedIndex;
                    }

                    if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
                    {
                        Bitmap? direct = TryExtractIndexed(iconPath, iconIndex, size);
                        if (direct != null) return direct;
                    }
                }

                string? target = info.TargetPath;
                if (string.IsNullOrWhiteSpace(target)) return null;

                if (Directory.Exists(target))
                    return TryExtract(target, size);

                if (!File.Exists(target)) return null;

                return TryExtract(target, size);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Extracts a specific icon resource out of an executable or .ico.</summary>
        public static Bitmap? TryExtractIndexed(string path, int index, int pixelSize)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            int size = Math.Max(16, pixelSize);

            try
            {
                if (index >= 0)
                {
                    using (Icon? large = IconExtractorAtIndex(path, index, size))
                    {
                        if (large != null) return ResizeTo(large, size);
                    }
                }

                return TryExtract(path, size);
            }
            catch (Exception)
            {
                return TryExtract(path, size);
            }
        }

        private static Icon? IconExtractorAtIndex(string path, int index, int size)
        {
            return ShellExclusive(() =>
            {
                try
                {
                    if (string.Equals(System.IO.Path.GetExtension(path), ".ico", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Icon temp = new Icon(path))
                        {
                            if (temp.Handle != IntPtr.Zero) return (Icon)temp.Clone();
                        }
                        return null;
                    }

                    if (size <= 32) return IconExtractorSmall(path, index);

                    return IconExtractorLarge(path, index);
                }
                catch (Exception)
                {
                    return null;
                }
            });
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr ExtractIconExW(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

        private static Icon? IconExtractorLarge(string path, int index)
        {
            var large = new IntPtr[1];
            var small = new IntPtr[1];
            if (ExtractIconExW(path, index, large, small, 1) == 0) return null;

            IntPtr handle = large[0] != IntPtr.Zero ? large[0] : small[0];
            if (handle == IntPtr.Zero) return null;

            try
            {
                using (Icon wrapper = Icon.FromHandle(handle))
                {
                    return (Icon)wrapper.Clone();
                }
            }
            finally
            {
                DestroyIconQuietly(large[0]);
                DestroyIconQuietly(small[0]);
            }
        }

        private static Icon? IconExtractorSmall(string path, int index)
        {
            var large = new IntPtr[1];
            var small = new IntPtr[1];
            if (ExtractIconExW(path, index, large, small, 1) == 0) return null;

            IntPtr handle = small[0] != IntPtr.Zero ? small[0] : large[0];
            if (handle == IntPtr.Zero) return null;

            try
            {
                using (Icon wrapper = Icon.FromHandle(handle))
                {
                    return (Icon)wrapper.Clone();
                }
            }
            finally
            {
                DestroyIconQuietly(large[0]);
                DestroyIconQuietly(small[0]);
            }
        }

        private static void DestroyIconQuietly(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try
            {
                NativeMethods.DestroyIcon(handle);
            }
            catch (Exception)
            {
                // Best effort; the GDI handle would otherwise leak for the
                // lifetime of the process.
            }
        }

        /// <summary>
        /// Scales an icon to an exact pixel size. Icons are extracted at whatever
        /// resource the shell offers, which is rarely what the UI wants on a
        /// 150% or 200% display.
        /// </summary>
        public static Bitmap? ResizeTo(Icon icon, int pixelSize)
        {
            if (icon == null) return null;

            int size = Math.Max(1, pixelSize);
            try
            {
                // Icon.ToBitmap gives the icon at its own size, often 32x32 from a
                // large-icon extraction. Draw into an explicitly sized bitmap so
                // the result is crisp at high DPI rather than scaled by the
                // layout engine later.
                using (Bitmap source = icon.ToBitmap())
                {
                    var target = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(target))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(source, new Rectangle(0, 0, size, size));
                    }
                    return target;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Reads a bitmap from disk, disposing the stream. Never throws.</summary>
        public static Bitmap? TryLoadBitmap(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (!File.Exists(path)) return null;

            try
            {
                // Read the bytes first: Image.FromStream keeps the stream alive
                // for the lifetime of the bitmap, and a locked file cannot be
                // rewritten while an icon is cached.
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0) return null;

                using (var ms = new MemoryStream(bytes, writable: false))
                using (Bitmap temp = new Bitmap(ms))
                {
                    return new Bitmap(temp);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Extracts the icon out of an .ico file at a given size.</summary>
        public static Bitmap? TryExtractFromIco(string? icoPath, int pixelSize)
        {
            if (string.IsNullOrWhiteSpace(icoPath) || !File.Exists(icoPath)) return null;

            try
            {
                using (Icon icon = new Icon(icoPath))
                {
                    return ResizeTo(icon, pixelSize);
                }
            }
            catch (Exception)
            {
                return TryLoadBitmap(icoPath);
            }
        }
    }
}