using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Windows.Shell;

namespace TaskbarGroups.Icons.Providers
{
    /// <summary>
    /// Shared provider behaviour: bounds checking and failure containment.
    /// </summary>
    /// <remarks>
    /// The rule every provider inherits is that <see cref="Extract"/> returns null
    /// rather than throwing. That is what turns the P0 crash class into a missing
    /// icon: the caller substitutes the fallback bitmap and the UI stays alive.
    /// </remarks>
    public abstract class IconProviderBase : IIconProvider
    {
        public abstract IEnumerable<ShortcutType> SupportedTypes { get; }

        public abstract bool CanProvide(ShortcutItem item);

        public virtual Bitmap? Extract(ShortcutItem item, IconRequest request)
        {
            if (item == null || request == null) return null;

            try
            {
                int size = Math.Max(16, Math.Min(request.PixelSize, 256));
                return ExtractCore(item, size);
            }
            catch (Exception)
            {
                // Any provider failure - a locked file, a corrupt resource, an
                // access-denied - degrades to the fallback icon.
                return null;
            }
        }

        protected abstract Bitmap? ExtractCore(ShortcutItem item, int pixelSize);
    }

    /// <summary>Icon of a native executable.</summary>
    public sealed class ExeIconProvider : IconProviderBase
    {
        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get { yield return ShortcutType.Exe; }
        }

        public override bool CanProvide(ShortcutItem item)
        {
            return item != null && item.Type == ShortcutType.Exe;
        }

        protected override Bitmap? ExtractCore(ShortcutItem item, int pixelSize)
        {
            string path = StringHelpers.ExpandPath(item.Target);

            // The guard the legacy code did not have: an executable that is not
            // there must not be handed to Icon.ExtractAssociatedIcon.
            if (!File.Exists(path) || Directory.Exists(path)) return null;

            return ShellIconExtractor.TryExtract(path, pixelSize);
        }
    }

    /// <summary>Icon of a Windows shell link, following the link to its real target.</summary>
    /// <remarks>
    /// Direct replacement for <c>frmGroup.handleLnkExt</c>. The original read
    /// <c>IconLocation</c> and <c>TargetPath</c> out of the link and passed them
    /// straight to <c>Icon.ExtractAssociatedIcon</c> without checking that either
    /// existed, was a file, or was a path rather than a URL. That produced the
    /// reported <see cref="FileNotFoundException"/> for a folder link, a moved
    /// target, and any shortcut whose icon location was an http address.
    /// </remarks>
    public sealed class LnkIconProvider : IconProviderBase
    {
        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get
            {
                yield return ShortcutType.Lnk;
                yield return ShortcutType.AppRefMs;
            }
        }

        public override bool CanProvide(ShortcutItem item)
        {
            return item != null && (item.Type == ShortcutType.Lnk || item.Type == ShortcutType.AppRefMs);
        }

        protected override Bitmap? ExtractCore(ShortcutItem item, int pixelSize)
        {
            string path = StringHelpers.ExpandPath(item.Target);
            if (!File.Exists(path)) return null;

            // TryExtractFromLink handles the icon location, the index, the
            // folder case and the missing-target case, and returns null when
            // nothing is available.
            Bitmap? fromLink = ShellIconExtractor.TryExtractFromLink(path, pixelSize);
            if (fromLink != null) return fromLink;

            // A link that resolves to nothing still has the shell's generic
            // shortcut icon, which is better than an error placeholder.
            return ShellIconExtractor.TryExtract(path, pixelSize);
        }
    }

    /// <summary>
    /// Icon of a folder.
    /// </summary>
    /// <remarks>
    /// Folders get a real folder icon rather than the result of calling
    /// <c>Icon.ExtractAssociatedIcon</c> on a directory. That call is what threw
    /// for folder shortcuts.
    /// </remarks>
    public sealed class FolderIconProvider : IconProviderBase
    {
        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get { yield return ShortcutType.Folder; }
        }

        public override bool CanProvide(ShortcutItem item)
        {
            return item != null && item.Type == ShortcutType.Folder;
        }

        protected override Bitmap? ExtractCore(ShortcutItem item, int pixelSize)
        {
            string path = StringHelpers.ExpandPath(item.Target);
            if (!Directory.Exists(path)) return null;

            return ShellIconExtractor.TryExtract(path, pixelSize);
        }
    }

    /// <summary>
    /// Icon taken from the shell's file-association rules.
    /// </summary>
    /// <remarks>
    /// Covers documents, media, and anything else with a registered extension.
    /// The legacy editor only offered .exe/.lnk/.url, so a dropped document or a
    /// dropped script had no icon path at all.
    /// </remarks>
    public sealed class FileAssociationIconProvider : IconProviderBase
    {
        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get
            {
                yield return ShortcutType.Bat;
                yield return ShortcutType.Cmd;
                yield return ShortcutType.Ps1;
                yield return ShortcutType.Unknown;
            }
        }

        public override bool CanProvide(ShortcutItem item)
        {
            if (item == null) return false;

            if (item.Type.IsScript()) return true;

            if (item.Type != ShortcutType.Unknown) return false;

            string extension = Path.GetExtension(item.Target ?? string.Empty);
            return extension.Length > 0;
        }

        protected override Bitmap? ExtractCore(ShortcutItem item, int pixelSize)
        {
            string path = StringHelpers.ExpandPath(item.Target);
            if (!File.Exists(path)) return null;

            return ShellIconExtractor.TryExtract(path, pixelSize);
        }
    }

    /// <summary>
    /// The last provider: a drawn placeholder that always succeeds.
    /// </summary>
    /// <remarks>
    /// The legacy application returned a bitmap too, but it built one by throwing
    /// and catching: <c>Icon.ExtractAssociatedIcon(path).ToBitmap()</c> would
    /// dereference a null Icon for any path with no associated icon, and the
    /// surrounding <c>catch</c> sometimes caught it and sometimes did not,
    /// depending on which caller. This one draws a rounded square with the
    /// target's initial, which is deterministic and cannot fail.
    /// </remarks>
    public sealed class FallbackIconProvider : IconProviderBase
    {
        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get
            {
                foreach (ShortcutType value in Enum.GetValues(typeof(ShortcutType)))
                    yield return value;
            }
        }

        public override bool CanProvide(ShortcutItem item) => true;

        protected override Bitmap? ExtractCore(ShortcutItem item, int pixelSize)
        {
            int size = Math.Max(16, Math.Min(pixelSize, 256));
            string label = InitialOf(item);

            var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float radius = size / 6f;
                using (var path = RoundedRectangle(new Rectangle(1, 1, size - 2, size - 2), radius))
                using (var brush = new SolidBrush(Color.FromArgb(90, 128, 128, 128)))
                using (var border = new Pen(Color.FromArgb(160, 160, 160, 160), Math.Max(1f, size / 32f)))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(border, path);
                }

                if (label.Length > 0)
                {
                    using (var font = new Font("Segoe UI", Math.Max(7f, size * 0.5f), FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var brush = new SolidBrush(Color.FromArgb(220, 240, 240, 240)))
                    using (var format = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    })
                    {
                        g.DrawString(label, font, brush, new RectangleF(0, 0, size, size), format);
                    }
                }
            }

            return bitmap;
        }

        private static string InitialOf(ShortcutItem? item)
        {
            string? source = item?.Name;
            if (string.IsNullOrWhiteSpace(source)) source = item?.Target;

            if (string.IsNullOrWhiteSpace(source)) return "?";

            source = source.Trim();

            // Take the first letter of the base name, not of a drive letter.
            string candidate = source;
            int slash = Math.Max(candidate.LastIndexOf('\\'), candidate.LastIndexOf('/'));
            if (slash >= 0 && slash + 1 < candidate.Length) candidate = candidate.Substring(slash + 1);

            candidate = candidate.Trim();
            return candidate.Length == 0 ? "?" : candidate.Substring(0, 1).ToUpperInvariant();
        }

        internal static GraphicsPath RoundedRectangle(Rectangle bounds, float radius)
        {
            var path = new GraphicsPath();

            float diameter = radius * 2f;
            diameter = Math.Min(diameter, Math.Min(bounds.Width, bounds.Height));

            if (diameter <= 0.5f)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}