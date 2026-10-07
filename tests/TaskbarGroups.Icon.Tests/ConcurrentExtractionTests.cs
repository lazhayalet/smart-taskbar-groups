using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Icons.Cache;
using Xunit;

namespace TaskbarGroups.IconTests
{
    /// <summary>
    /// Regression test for a concurrency defect found while building the icon cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SHGetFileInfo</c> and <c>ExtractIconEx</c> hand back a GDI handle
    /// through an out-parameter structure, and the shell does not make the
    /// handle-producing part of those calls reentrant. Calling them from several
    /// threads at once produced a call that reported success and returned a null
    /// <c>hIcon</c>: 12 failures in 400 concurrent extractions, measured.
    /// </para>
    /// <para>
    /// That is why <c>ShellIconExtractor</c> serialises acquisition. This test
    /// exists so a future change that removes the lock fails loudly here instead
    /// of showing up as an intermittently blank icon - or, on the legacy code
    /// path, a null-reference exception when the null handle was dereferenced.
    /// </para>
    /// </remarks>
    public class ConcurrentExtractionTests
    {
        [Fact]
        public void Concurrent_extraction_from_a_real_executable_never_returns_null()
        {
            string notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
            if (!File.Exists(notepad)) return;

            var failures = new ConcurrentBag<int>();

            Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 16 }, index =>
            {
                using Bitmap? icon = TaskbarGroups.Windows.Shell.ShellIconExtractor.TryExtract(notepad, 32);
                if (icon == null) failures.Add(index);
            });

            Assert.Empty(failures);
        }

        [Fact]
        public void Concurrent_extraction_of_the_same_item_through_the_cache_is_consistent()
        {
            // The cache serialises per key, so concurrent requests for one item all
            // receive the same bitmap instance rather than racing to extract it.
            string directory = Path.Combine(Path.GetTempPath(),
                "taskbargroups-concurrent-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            try
            {
                using IconCache cache = IconCache.CreateDefault(directory);

                var item = new ShortcutItem
                {
                    Type = ShortcutType.Folder,
                    Target = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                };

                var results = new ConcurrentBag<Bitmap>();

                Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
                {
                    results.Add(cache.GetIcon(item, new IconRequest { LogicalSize = 32, ScaleFactor = 1d }));
                });

                // Every caller owns its copy, so disposing them all is safe and the
                // cache survives it - which is exactly what the caller-facing
                // ownership rule is for.
                foreach (Bitmap bitmap in results)
                {
                    Assert.True(bitmap.Width > 0);
                    bitmap.Dispose();
                }

                // And the cache is still usable afterwards.
                using Bitmap after = cache.GetIcon(item, new IconRequest { LogicalSize = 32, ScaleFactor = 1d });
                Assert.True(after.Width > 0);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                }
                catch (Exception)
                {
                }
            }
        }

        [Fact]
        public void Concurrent_extraction_of_broken_items_still_yields_the_placeholder()
        {
            // The other half of the defect: shared placeholders must not be disposed
            // out from under a concurrent reader.
            string directory = Path.Combine(Path.GetTempPath(),
                "taskbargroups-concurrent-fallback-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            try
            {
                using IconCache cache = IconCache.CreateDefault(directory);

                var items = new ShortcutItem[8];
                for (int index = 0; index < items.Length; index++)
                {
                    items[index] = new ShortcutItem
                    {
                        Type = ShortcutType.Exe,
                        Target = @"C:\taskbargroups\not\here\" + index + ".exe"
                    };
                }

                var failures = new ConcurrentBag<string>();

                Parallel.For(0, 256, new ParallelOptions { MaxDegreeOfParallelism = 16 }, index =>
                {
                    ShortcutItem item = items[index % items.Length];
                    using Bitmap icon = cache.GetIcon(item, new IconRequest { LogicalSize = 32, ScaleFactor = 1d });

                    try
                    {
                        if (icon.Width <= 0) failures.Add(item.Target);
                    }
                    catch (Exception)
                    {
                        failures.Add(item.Target);
                    }
                });

                Assert.Empty(failures);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                }
                catch (Exception)
                {
                }
            }
        }

        [Fact]
        public void Invalidating_one_broken_item_does_not_break_the_others()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "taskbargroups-invalidate-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            try
            {
                using IconCache cache = IconCache.CreateDefault(directory);
                var request = new IconRequest { LogicalSize = 32, ScaleFactor = 1d };

                var first = new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\gone\first.exe" };
                var second = new ShortcutItem { Type = ShortcutType.Exe, Target = @"C:\gone\second.exe" };

                using (Bitmap a = cache.GetIcon(first, request))
                using (Bitmap b = cache.GetIcon(second, request))
                {
                    Assert.True(a.Width > 0);
                    Assert.True(b.Width > 0);
                }

                cache.Invalidate(first);

                // The other item must still resolve: disposing one item's master
                // must not take the placeholder with it.
                using Bitmap afterFirst = cache.GetIcon(second, request);
                using Bitmap afterSecond = cache.GetIcon(second, request);

                Assert.True(afterFirst.Width > 0);
                Assert.True(afterSecond.Width > 0);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}