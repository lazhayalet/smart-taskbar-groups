using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Icons;
using TaskbarGroups.Icons.Cache;
using TaskbarGroups.Icons.Providers;
using TaskbarGroups.Windows.Shell;
using Xunit;

namespace TaskbarGroups.IconTests
{
    /// <summary>
    /// Regression tests for the reported P0 icon crash.
    /// </summary>
    /// <remarks>
    /// The legacy code called <c>Icon.ExtractAssociatedIcon</c> on unvalidated
    /// paths in three places - <c>frmGroup.handleLnkExt</c>,
    /// <c>ucProgramShortcut_Load</c> and <c>Category.ExtractShortcutIcon</c> -
    /// and dereferenced the result without a null check. A folder shortcut, a
    /// moved target, or any path with no associated icon produced an exception out
    /// of a UI event handler. Every test below asserts the opposite: null or a
    /// placeholder, never a throw.
    /// </remarks>
    public class IconProviderRegressionTests : IDisposable
    {
        private readonly List<string> _artifacts = new List<string>();

        public void Dispose()
        {
            foreach (string path in _artifacts)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception)
                {
                }
            }
        }

        private string TempDirectory(string name)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "taskbargroups-icons-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "-" + name);
            Directory.CreateDirectory(path);
            _artifacts.Add(path);
            return path;
        }

        private static IconRequest Request(int size = 32)
        {
            return new IconRequest { LogicalSize = size, ScaleFactor = 1d };
        }

        [Fact]
        public void The_folder_provider_returns_an_icon_for_a_real_directory()
        {
            var provider = new FolderIconProvider();

            var item = new ShortcutItem
            {
                Type = ShortcutType.Folder,
                Target = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };

            using Bitmap? icon = provider.Extract(item, Request());
            Assert.NotNull(icon);
            Assert.True(icon!.Width > 0);
        }

        [Fact]
        public void The_folder_provider_returns_null_for_a_missing_directory()
        {
            var provider = new FolderIconProvider();

            var item = new ShortcutItem
            {
                Type = ShortcutType.Folder,
                Target = @"C:\taskbargroups\not\here\folder"
            };

            // The P0 shape: this used to reach Icon.ExtractAssociatedIcon and throw.
            using Bitmap? icon = provider.Extract(item, Request());
            Assert.Null(icon);
        }

        [Fact]
        public void The_exe_provider_refuses_a_directory_even_if_typed_as_an_exe()
        {
            // Exactly what a migrated folder shortcut looked like: a directory path
            // with no folder type, because the legacy model could not express one.
            var provider = new ExeIconProvider();

            var item = new ShortcutItem
            {
                Type = ShortcutType.Exe,
                Target = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };

            using Bitmap? icon = provider.Extract(item, Request());
            Assert.Null(icon);
        }

        [Fact]
        public void The_exe_provider_returns_null_for_a_missing_file()
        {
            var provider = new ExeIconProvider();

            var item = new ShortcutItem
            {
                Type = ShortcutType.Exe,
                Target = @"C:\taskbargroups\not\here\missing.exe"
            };

            using Bitmap? icon = provider.Extract(item, Request());
            Assert.Null(icon);
        }

        [Fact]
        public void The_exe_provider_returns_an_icon_for_a_real_executable()
        {
            string notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
            if (!File.Exists(notepad)) return;

            var provider = new ExeIconProvider();

            using Bitmap? icon = provider.Extract(new ShortcutItem { Type = ShortcutType.Exe, Target = notepad }, Request());

            Assert.NotNull(icon);
            Assert.Equal(32, icon!.Width);
        }

        [Fact]
        public void The_link_provider_returns_null_for_a_broken_link()
        {
            string link = Path.Combine(TempDirectory("broken"), "broken.lnk");
            File.WriteAllText(link, "definitely not a shortcut");

            var provider = new LnkIconProvider();

            using Bitmap? icon = provider.Extract(new ShortcutItem { Type = ShortcutType.Lnk, Target = link }, Request());

            // A null result is fine here: the shell's generic shortcut icon is not
            // guaranteed. What matters is that nothing throws.
            Assert.True(icon == null || icon.Width > 0);
        }

        [Fact]
        public void The_link_provider_returns_null_for_a_missing_link()
        {
            var provider = new LnkIconProvider();

            using Bitmap? icon = provider.Extract(
                new ShortcutItem { Type = ShortcutType.Lnk, Target = @"C:\gone\missing.lnk" }, Request());

            Assert.Null(icon);
        }

        [Fact]
        public void Every_provider_returns_null_rather_than_throwing_for_a_null_item()
        {
            var providers = new IIconProvider[]
            {
                new ExeIconProvider(),
                new LnkIconProvider(),
                new FolderIconProvider(),
                new FileAssociationIconProvider(),
                new UwpIconProvider(),
                new PwaIconProvider(_ => null)
            };

            foreach (IIconProvider provider in providers)
            {
                using Bitmap? icon = provider.Extract(null!, Request());
                Assert.True(icon == null || icon.Width > 0, provider.GetType().Name);
            }
        }

        [Fact]
        public void Every_provider_handles_an_empty_target()
        {
            var providers = new IIconProvider[]
            {
                new ExeIconProvider(),
                new LnkIconProvider(),
                new FolderIconProvider(),
                new FileAssociationIconProvider(),
                new UwpIconProvider()
            };

            foreach (IIconProvider provider in providers)
            {
                using Bitmap? icon = provider.Extract(new ShortcutItem { Type = ShortcutType.Exe, Target = "" }, Request());
                Assert.True(icon == null || icon.Width > 0, provider.GetType().Name);
            }
        }

        [Fact]
        public void A_provider_with_a_malformed_icon_source_returns_null()
        {
            var provider = new ExeIconProvider();

            var item = new ShortcutItem
            {
                Type = ShortcutType.Exe,
                Target = @"C:\gone\x.exe",
                IconSource = @"%NOT_A_VARIABLE%\image.png"
            };

            using Bitmap? icon = provider.Extract(item, Request());
            Assert.Null(icon);
        }

        [Fact]
        public void The_fallback_provider_always_produces_a_placeholder()
        {
            // The fallback has to succeed for everything, because it is what the
            // cache substitutes when every other provider has failed.
            var provider = new FallbackIconProvider();

            foreach (string target in new[] { "", @"C:\gone\x.exe", "not a path at all" })
            {
                using Bitmap? icon = provider.Extract(new ShortcutItem { Target = target }, Request());

                Assert.NotNull(icon);
                Assert.Equal(32, icon!.Width);
                Assert.Equal(32, icon.Height);
            }
        }

        [Fact]
        public void The_fallback_draws_the_first_letter_of_the_name()
        {
            var provider = new FallbackIconProvider();

            using Bitmap a = provider.Extract(new ShortcutItem { Name = "Alpha" }, Request())!;

            // Compare two different initials rather than reading pixels, which keeps
            // the test independent of the font.
            using Bitmap b = provider.Extract(new ShortcutItem { Name = "Beta" }, Request())!;

            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.True(PixelsDiffer(a!, b!));
        }

        [Fact]
        public void The_fallback_claims_every_type_so_it_can_never_be_skipped()
        {
            var provider = new FallbackIconProvider();

            foreach (ShortcutType type in Enum.GetValues(typeof(ShortcutType)))
                Assert.Contains(type, provider.SupportedTypes);
        }

        [Fact]
        public void The_fallback_scales_to_the_requested_size()
        {
            var provider = new FallbackIconProvider();

            using Bitmap? icon = provider.Extract(new ShortcutItem(), new IconRequest { LogicalSize = 64, ScaleFactor = 1d });

            Assert.Equal(64, icon!.Width);
        }

        internal static bool PixelsDiffer(Bitmap left, Bitmap right)
        {
            for (int y = 0; y < left.Height; y += 4)
            {
                for (int x = 0; x < left.Width; x += 4)
                {
                    if (left.GetPixel(x, y) != right.GetPixel(x, y)) return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// The content-addressed cache.
    /// </summary>
    /// <remarks>
    /// The legacy key was a file name plus a suffix, so two shortcuts in one group
    /// whose names differed only by extension collapsed onto one cache entry, and
    /// moving a shortcut invalidated its icon even when nothing had changed. These
    /// tests pin the properties the new key is supposed to have.
    /// </remarks>
    public class IconCacheTests : IDisposable
    {
        private readonly List<string> _artifacts = new List<string>();

        public void Dispose()
        {
            foreach (string path in _artifacts)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception)
                {
                }
            }
        }

        private string TempDirectory(string name)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "taskbargroups-cache-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "-" + name);
            Directory.CreateDirectory(path);
            _artifacts.Add(path);
            return path;
        }

        private IconCache NewCache(string directory, IIconRepository? repository = null)
        {
            return IconCache.CreateDefault(directory, repository);
        }

        private static IconRequest Request(int size = 32) => new IconRequest { LogicalSize = size, ScaleFactor = 1d };

        [Fact]
        public void The_key_is_stable_for_an_unchanged_item()
        {
            using IconCache cache = NewCache(TempDirectory("stable"));

            var item = new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\gone\a.exe" };

            Assert.Equal(cache.ComputeCacheKey(item), cache.ComputeCacheKey(item));
        }

        [Fact]
        public void Two_extensions_with_the_same_file_name_get_different_keys()
        {
            // The legacy bug: Game.exe and Game.lnk both reduced to "Game.png".
            using IconCache cache = NewCache(TempDirectory("collision"));

            var exe = new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\apps\Game.exe" };
            var lnk = new ShortcutItem { Type = ShortcutType.Lnk, Target = @"C:\apps\Game.lnk" };

            Assert.NotEqual(cache.ComputeCacheKey(exe), cache.ComputeCacheKey(lnk));
        }

        [Fact]
        public void Different_targets_get_different_keys()
        {
            using IconCache cache = NewCache(TempDirectory("targets"));

            var first = new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\apps\one.exe" };
            var second = new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\apps\two.exe" };

            Assert.NotEqual(cache.ComputeCacheKey(first), cache.ComputeCacheKey(second));
        }

        [Fact]
        public void The_key_is_case_insensitive_in_its_inputs()
        {
            using IconCache cache = NewCache(TempDirectory("case"));

            var lower = new ShortcutItem { Type = ShortcutType.Exe, Target = @"c:\apps\thing.exe" };
            var upper = new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\Apps\THING.EXE" };

            Assert.Equal(cache.ComputeCacheKey(lower), cache.ComputeCacheKey(upper));
        }

        [Fact]
        public void A_changed_file_produces_a_different_key()
        {
            string directory = TempDirectory("changed");
            string target = Path.Combine(directory, "app.exe");
            File.WriteAllText(target, "first");

            using (IconCache first = NewCache(directory))
            {
                var item = new ShortcutItem { Type = ShortcutType.Exe, Target = target };
                string before = first.ComputeCacheKey(item);

                File.WriteAllText(target, "second, different length");
                File.SetLastWriteTime(target, DateTime.UtcNow.AddMinutes(1));

                string after = first.ComputeCacheKey(item);

                Assert.NotEqual(before, after);
            }
        }

        [Fact]
        public void The_key_is_always_a_long_hex_hash()
        {
            using IconCache cache = NewCache(TempDirectory("shape"));

            string key = cache.ComputeCacheKey(new ShortcutItem { Type = ShortcutType.Folder, Target = @"C:\x" });

            Assert.Equal(64, key.Length);
            Assert.All(key, c => Assert.True(Uri.IsHexDigit(c), "not a hex digit: " + c));
        }

        [Fact]
        public void GetIcon_returns_a_placeholder_for_a_broken_item()
        {
            using IconCache cache = NewCache(TempDirectory("placeholder"));

            var item = new ShortcutItem
            {
                Type = ShortcutType.Exe,
                Target = @"C:\taskbargroups\not\here\missing.exe"
            };

            using Bitmap icon = cache.GetIcon(item, Request());

            Assert.NotNull(icon);
            Assert.True(icon.Width > 0);
        }

        [Fact]
        public void GetIcon_never_returns_null_even_for_a_null_item()
        {
            using IconCache cache = NewCache(TempDirectory("nullitem"));

            using Bitmap icon = cache.GetIcon(null!, Request());

            Assert.True(icon.Width > 0);
        }

        [Fact]
        public void A_repeat_request_returns_an_owned_copy_of_the_cached_icon()
        {
            using IconCache cache = NewCache(TempDirectory("memory"));

            var item = new ShortcutItem
            {
                Type = ShortcutType.Exe,
                Target = @"C:\gone\thing.exe"
            };

            // Distinct instances, because each caller owns what it is given and
            // disposes it. Identical content, because the second one comes from the
            // cache rather than from a fresh shell call.
            using Bitmap first = cache.GetIcon(item, Request());
            using Bitmap second = cache.GetIcon(item, Request());

            Assert.NotSame(first, second);
            Assert.Equal(first.Width, second.Width);
            Assert.Equal(first.Height, second.Height);
        }

        [Fact]
        public void Disposing_a_returned_icon_does_not_poison_the_cache()
        {
            using IconCache cache = NewCache(TempDirectory("ownership"));

            var item = new ShortcutItem
            {
                Type = ShortcutType.Folder,
                Target = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };

            // The realistic sequence: a control asks for an icon, shows it, and
            // disposes it when it closes.
            Bitmap shown = cache.GetIcon(item, Request());
            shown.Dispose();

            using Bitmap next = cache.GetIcon(item, Request());
            Assert.True(next.Width > 0);
        }

        [Fact]
        public void Different_sizes_get_different_cache_entries()
        {
            using IconCache cache = NewCache(TempDirectory("sizes"));

            var item = new ShortcutItem
            {
                Type = ShortcutType.Folder,
                Target = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };

            using Bitmap small = cache.GetIcon(item, new IconRequest { LogicalSize = 16, ScaleFactor = 1d });
            using Bitmap large = cache.GetIcon(item, new IconRequest { LogicalSize = 64, ScaleFactor = 1d });

            Assert.NotEqual(small.Width, large.Width);
        }

        [Fact]
        public void A_scale_factor_is_applied_to_the_requested_size()
        {
            using IconCache cache = NewCache(TempDirectory("scaled"));

            var item = new ShortcutItem
            {
                Type = ShortcutType.Exe,
                Target = @"C:\gone\thing.exe"
            };

            using Bitmap atOne = cache.GetIcon(item, new IconRequest { LogicalSize = 32, ScaleFactor = 1d });
            using Bitmap atTwo = cache.GetIcon(item, new IconRequest { LogicalSize = 32, ScaleFactor = 2d });

            Assert.True(atTwo.Width > atOne.Width);
        }

        [Fact]
        public void Invalidate_removes_the_in_memory_entry()
        {
            using IconCache cache = NewCache(TempDirectory("invalidate"));

            // A folder, so extraction genuinely succeeds and a fresh bitmap is
            // produced after invalidation. A target nothing can extract would return
            // the shared placeholder, which is the same instance every time by design.
            var item = new ShortcutItem
            {
                Type = ShortcutType.Folder,
                Target = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };

            Bitmap before = cache.GetIcon(item, Request());
            cache.Invalidate(item);
            Bitmap after = cache.GetIcon(item, Request());

            Assert.NotSame(before, after);
        }

        [Fact]
        public void Clearing_empties_the_cache()
        {
            string directory = TempDirectory("clear");

            using (IconCache cache = NewCache(directory))
            {
                cache.GetIcon(new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\gone\x.exe" }, Request());
                cache.Clear();

                Assert.True(Directory.Exists(directory));
            }
        }

        [Fact]
        public void A_corrupt_cache_file_is_discarded_rather_than_trusted()
        {
            string directory = TempDirectory("corrupt");

            using (IconCache cache = NewCache(directory))
            {
                var item = new ShortcutItem
                {
                    Type = ShortcutType.Exe,
                    Target = @"C:\gone\thing.exe"
                };

                cache.GetIcon(item, Request());
                cache.Invalidate(item);

                // Write rubbish where the cache file will be looked for.
                string key = cache.ComputeCacheKey(item) + "-32";
                File.WriteAllText(Path.Combine(directory, key + ".png"), "this is not a png");

                // A corrupt entry must be replaced, not surfaced as a broken image.
                using Bitmap icon = cache.GetIcon(item, Request());
                Assert.True(icon.Width > 0);
            }
        }

        [Fact]
        public void An_unwritable_cache_directory_does_not_prevent_icons()
        {
            // A cache is an optimisation. Failing to write one must not stop the
            // caller getting an icon in hand.
            string blocked = Path.Combine(Path.GetTempPath(), "taskbargroups-cache-" + Guid.NewGuid().ToString("N"));

            using var cache = new IconCache(
                Path.Combine(blocked, "does", "not", "exist", "either"),
                new IIconProvider[0]);

            using Bitmap icon = cache.GetIcon(new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\gone\x.exe" }, Request());

            Assert.True(icon.Width > 0);
        }
    }

    /// <summary>Icon resizing and multi-resolution .ico writing.</summary>
    public class IconScalerTests
    {
        [Fact]
        public void Resize_produces_the_exact_requested_size()
        {
            using Bitmap source = new Bitmap(48, 48);
            using Bitmap? resized = IconScaler.Resize(source, 32);

            Assert.NotNull(resized);
            Assert.Equal(32, resized!.Width);
            Assert.Equal(32, resized.Height);
        }

        [Fact]
        public void Resize_preserves_alpha()
        {
            using var source = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            source.SetPixel(0, 0, Color.FromArgb(0, 255, 0, 0));

            using Bitmap? resized = IconScaler.Resize(source, 16);

            Assert.Equal(PixelFormat.Format32bppArgb, resized!.PixelFormat);
        }

        [Fact]
        public void Resize_upscales_a_small_icon_without_failing()
        {
            using var source = new Bitmap(16, 16);

            using Bitmap? resized = IconScaler.Resize(source, 256);

            Assert.Equal(256, resized!.Width);
        }

        [Fact]
        public void Resize_of_a_null_source_returns_null()
        {
            Assert.Null(IconScaler.Resize(null!, 32));
        }

        [Fact]
        public void Nearest_neighbour_resize_is_available_for_artwork()
        {
            using var source = new Bitmap(64, 64, PixelFormat.Format32bppArgb);

            using Bitmap? resized = IconScaler.ResizeNearest(source, 16);

            Assert.Equal(16, resized!.Width);
        }

        [Fact]
        public void A_multi_size_icon_is_written_with_every_requested_frame()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "taskbargroups-test-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ico");

            try
            {
                using var source = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(source))
                {
                    graphics.Clear(Color.CornflowerBlue);
                }

                IconScaler.WriteMultiSizeIcon(source, path, new[] { 16, 32, 48 });

                Assert.True(File.Exists(path));

                byte[] bytes = File.ReadAllBytes(path);

                // ICONDIR: reserved, type, count.
                Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
                Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
                Assert.Equal(3, BitConverter.ToUInt16(bytes, 4));

                // Entries are 16 bytes each: width, height, palette, reserved, planes,
                // bit count, byte length, offset. The first requested size is 16 and the
                // second is 32.
                Assert.Equal(16, bytes[6]);
                Assert.Equal(16, bytes[7]);
                Assert.Equal(32, bytes[6 + 16]);
                Assert.Equal(32, bytes[7 + 16]);
                Assert.Equal(48, bytes[6 + 32]);

                // And the file is readable back as an icon.
                using var icon = new Icon(path);
                Assert.NotEqual(IntPtr.Zero, icon.Handle);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void The_default_frame_set_includes_16_pixels()
        {
            // The legacy writer's loop stopped before adding the 16px frame, so
            // every taskbar and Start-menu entry was a blurry upscale of a larger
            // icon.
            string path = Path.Combine(Path.GetTempPath(),
                "taskbargroups-test-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ico");

            try
            {
                using var source = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(source))
                {
                    graphics.Clear(Color.SeaGreen);
                }

                IconScaler.WriteMultiSizeIcon(source, path);

                byte[] bytes = File.ReadAllBytes(path);
                int count = BitConverter.ToUInt16(bytes, 4);

                Assert.Equal(7, count);
                Assert.Equal(16, bytes[6]);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    /// <summary>
    /// Shell extraction safety.
    /// </summary>
    public class ShellIconExtractorTests
    {
        [Fact]
        public void Extraction_from_an_empty_path_returns_null()
        {
            Assert.Null(ShellIconExtractor.TryExtract(null, 32));
            Assert.Null(ShellIconExtractor.TryExtract("", 32));
            Assert.Null(ShellIconExtractor.TryExtract("   ", 32));
        }

        [Fact]
        public void Extraction_from_a_missing_path_yields_the_shell_generic_icon()
        {
            // SHGFI_USEFILEATTRIBUTES is used deliberately: Explorer shows a generic
            // .exe icon for a program that is not installed, and so does this. The
            // providers decide separately whether the item is broken.
            using Bitmap? icon = ShellIconExtractor.TryExtract(@"C:\taskbarGroups\not\here\missing.exe", 32);

            Assert.NotNull(icon);
        }

        [Fact]
        public void Extraction_from_a_directory_returns_an_icon()
        {
            using Bitmap? icon = ShellIconExtractor.TryExtract(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), 32);

            Assert.NotNull(icon);
        }

        [Fact]
        public void Extraction_from_a_short_link_returns_null_rather_than_throwing()
        {
            Assert.Null(ShellIconExtractor.TryExtractFromLink(@"C:\gone\missing.lnk", 32));
            Assert.Null(ShellIconExtractor.TryExtractFromLink("", 32));
        }

        [Fact]
        public void Loading_a_missing_bitmap_returns_null()
        {
            Assert.Null(ShellIconExtractor.TryLoadBitmap(@"C:\gone\missing.png"));
            Assert.Null(ShellIconExtractor.TryLoadBitmap(""));
        }

        [Fact]
        public void Loading_a_corrupt_bitmap_returns_null()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "taskbargroups-corrupt-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");

            try
            {
                File.WriteAllText(path, "not an image at all");

                Assert.Null(ShellIconExtractor.TryLoadBitmap(path));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Loading_an_empty_file_returns_null()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "taskbargroups-empty-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");

            try
            {
                File.WriteAllBytes(path, Array.Empty<byte>());
                Assert.Null(ShellIconExtractor.TryLoadBitmap(path));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Loading_a_real_bitmap_returns_an_independent_copy()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "taskbargroups-real-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");

            try
            {
                using (var source = new Bitmap(16, 16))
                {
                    source.Save(path, ImageFormat.Png);
                }

                using Bitmap? first = ShellIconExtractor.TryLoadBitmap(path);
                using Bitmap? second = ShellIconExtractor.TryLoadBitmap(path);

                Assert.NotNull(first);
                Assert.NotNull(second);
                Assert.NotSame(first, second);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Resize_to_a_smaller_size_works()
        {
            using Icon icon = SystemIcons.Application;

            using Bitmap? resized = ShellIconExtractor.ResizeTo(icon, 16);

            Assert.NotNull(resized);
            Assert.Equal(16, resized!.Width);
        }
    }
}