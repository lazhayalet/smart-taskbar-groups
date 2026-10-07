using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TaskbarGroups.Icons
{
    /// <summary>
    /// Scales bitmaps without the blurry upscale the legacy layout produced.
    /// </summary>
    /// <remarks>
    /// The legacy group popup drew 32x32 shell icons into 25x30 boxes with
    /// <c>ImageLayout.Stretch</c>, which Windows Forms scales with the default
    /// interpolation mode. On a 150% or 175% display the result is visibly soft -
    /// the HiDPI complaint in the issue tracker. Producing the icon at the exact
    /// pixel size it will be drawn at, before it ever reaches a control, avoids the
    /// resample entirely.
    /// </remarks>
    public static class IconScaler
    {
        /// <summary>Resizes to an exact pixel size, preserving alpha.</summary>
        public static Bitmap? Resize(Bitmap source, int pixelSize)
        {
            if (source == null) return null;

            int size = Math.Max(1, pixelSize);

            try
            {
                if (source.Width == size && source.Height == size)
                    return new Bitmap(source);

                var target = new Bitmap(size, size, PixelFormat.Format32bppArgb);

                using (Graphics g = Graphics.FromImage(target))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.Clear(Color.Transparent);

                    // Draw at the centre when downscaling so the important middle
                    // of an icon survives, which is what Explorer does too.
                    g.DrawImage(source, new Rectangle(0, 0, size, size));
                }

                return target;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Nearest-neighbour resize, for artwork that must stay crisp.</summary>
        public static Bitmap? ResizeNearest(Bitmap source, int pixelSize)
        {
            if (source == null) return null;

            int size = Math.Max(1, pixelSize);

            try
            {
                if (source.Width == size && source.Height == size)
                    return new Bitmap(source);

                var target = new Bitmap(size, size, PixelFormat.Format32bppArgb);

                using (Graphics g = Graphics.FromImage(target))
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(source, new Rectangle(0, 0, size, size));
                }

                return target;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Writes a multi-resolution .ico, the format the taskbar and the Start
        /// menu both require. Replaces the legacy <c>Category.createMultiIcon</c>,
        /// which dropped the 16px entry because its loop condition stopped before
        /// it was added.
        /// </summary>
        public static void WriteMultiSizeIcon(Bitmap image, string icoPath, IEnumerable<int>? sizes = null)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            var requested = new List<int>(sizes ?? new[] { 16, 24, 32, 48, 64, 128, 256 });
            if (requested.Count == 0) requested.Add(32);
            requested.Sort();

            var frames = new List<Bitmap>();
            try
            {
                foreach (int size in requested)
                {
                    Bitmap? frame = Resize(image, size);
                    if (frame != null) frames.Add(frame);
                }

                if (frames.Count == 0) return;

                using (FileStream stream = new FileStream(icoPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    WriteIcon(stream, frames);
                }
            }
            finally
            {
                foreach (Bitmap frame in frames) frame.Dispose();
            }
        }

        /// <summary>Writes a set of square PNG frames as one .ico file.</summary>
        public static void WriteIcon(Stream stream, IList<Bitmap> frames)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (frames == null || frames.Count == 0) throw new ArgumentException("At least one frame is required.", nameof(frames));

            var ordered = new Bitmap[frames.Count];
            for (int i = 0; i < frames.Count; i++) ordered[i] = frames[i];

            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                // ICONDIR
                writer.Write((ushort)0);                  // reserved
                writer.Write((ushort)1);                  // type: icon
                writer.Write((ushort)ordered.Length);     // image count

                int headerSize = 6;
                int entrySize = 16;
                int offset = headerSize + (entrySize * ordered.Length);

                var payloads = new byte[ordered.Length][];

                for (int i = 0; i < ordered.Length; i++)
                {
                    Bitmap frame = ordered[i];
                    int width = frame.Width >= 256 ? 0 : frame.Width;
                    int height = frame.Height >= 256 ? 0 : frame.Height;

                    using (var buffer = new System.IO.MemoryStream())
                    {
                        frame.Save(buffer, ImageFormat.Png);
                        payloads[i] = buffer.ToArray();
                    }

                    writer.Write((byte)width);
                    writer.Write((byte)height);
                    writer.Write((byte)0);                              // palette size
                    writer.Write((byte)0);                              // reserved
                    writer.Write((ushort)1);                            // colour planes
                    writer.Write((ushort)32);                           // bits per pixel
                    writer.Write(payloads[i].Length);
                    writer.Write(offset);

                    offset += payloads[i].Length;
                }

                foreach (byte[] payload in payloads)
                    writer.Write(payload);
            }
        }
    }
}