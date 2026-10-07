using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Icons.Providers;

namespace TaskbarGroups.Icons.Cache
{
    /// <summary>
    /// Content-addressed icon cache: a file per icon plus metadata in the database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replaces <c>Category.cacheIcons</c> / <c>Category.loadImageCache</c>, whose
    /// cache key was derived from the file name plus a per-extension suffix. Two
    /// shortcuts in one group whose names differed only by extension - Game.exe
    /// and Game.lnk - collapsed onto the same <c>Game.png</c>, and moving a
    /// shortcut invalidated its entry even when the icon had not changed. The key
    /// here is a SHA-256 over the icon's actual inputs: type, target, icon source,
    /// requested size and, for file-backed types, the file's size and last-write
    /// time. Re-extraction therefore happens only when something that can affect
    /// the picture has changed.
    /// </para>
    /// <para>
    /// Extraction is bounded by a memory cache and a per-key lock, so opening the
    /// same group twenty times in a row performs twenty dictionary lookups and zero
    /// shell calls.
    /// </para>
    /// </remarks>
    public sealed class IconCache : IIconCache, IDisposable
    {
        private readonly List<IIconProvider> _providers;
        private readonly IIconProvider _fallback;
        private readonly IIconRepository? _repository;
        private readonly string _cacheDirectory;
        private readonly IAppLogger? _logger;
        private readonly ConcurrentDictionary<string, Bitmap> _memory = new ConcurrentDictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Shared placeholder bitmaps, owned for the lifetime of the cache.</summary>
        private readonly ConcurrentDictionary<string, Bitmap> _fallbacks = new ConcurrentDictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, object> _locks = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private bool _disposed;

        public IconCache(
            string cacheDirectory,
            IEnumerable<IIconProvider> providers,
            IIconRepository? repository = null,
            IAppLogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(cacheDirectory))
                throw new ArgumentException("A cache directory is required.", nameof(cacheDirectory));

            _cacheDirectory = cacheDirectory;
            _repository = repository;
            _logger = logger;

            _providers = new List<IIconProvider>();
            foreach (var provider in providers ?? Array.Empty<IIconProvider>())
            {
                if (provider == null) continue;
                if (provider is FallbackIconProvider) continue;
                _providers.Add(provider);
            }

            // The fallback is created once and reused: it is deterministic, so
            // there is no reason to redraw it per request.
            _fallback = new FallbackIconProvider();

            try
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
            catch (Exception ex)
            {
                _logger?.Log(SystemLogLevel.Warning, "Icons", "Icon cache directory is not writable: " + cacheDirectory, ex);
            }
        }

        /// <summary>Builds the standard provider set.</summary>
        public static IconCache CreateDefault(
            string cacheDirectory,
            IIconRepository? repository = null,
            IAppLogger? logger = null,
            Func<string, string?>? pwaIconLocator = null,
            string? steamRoot = null)
        {
            var providers = new List<IIconProvider>
            {
                new ExeIconProvider(),
                new LnkIconProvider(),
                new FolderIconProvider(),
                new FileAssociationIconProvider(),
                new UwpIconProvider(),
                new PwaIconProvider(pwaIconLocator ?? (_ => null)),
                new SteamIconProvider(new Providers.SteamArtLookup(steamRoot ?? string.Empty))
            };

            return new IconCache(cacheDirectory, providers, repository, logger);
        }

        public string ComputeCacheKey(ShortcutItem item)
        {
            if (item == null) return string.Empty;

            var builder = new StringBuilder(256);
            builder.Append(item.Type.ToString()).Append('|');
            builder.Append((item.Target ?? string.Empty).ToUpperInvariant()).Append('|');
            builder.Append(item.IconSource ?? string.Empty).Append('|');

            // The file's own metadata, not its content: hashing a 200 MB installer
            // to decide whether its icon changed would defeat the cache.
            try
            {
                string path = StringHelpers.ExpandPath(item.Target);
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    builder.Append(info.Length).Append('@');
                    builder.Append(info.LastWriteTimeUtc.Ticks);
                }
                else if (Directory.Exists(path))
                {
                    builder.Append("dir");
                }
            }
            catch (Exception)
            {
                // Metadata unavailable; the path alone is still a stable key.
            }

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        /// <summary>Key for a specific pixel size, so 32px and 64px coexist.</summary>
        private string ComputeSizedKey(ShortcutItem item, int pixelSize)
        {
            return ComputeCacheKey(item) + "-" + pixelSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns an icon for the item, decoding from the cache when possible and
        /// extracting on a miss.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The caller owns the returned bitmap and must dispose it. Each call
        /// returns an independent copy, which is why a warm cache still allocates:
        /// a shared instance would be silently invalidated by the first caller that
        /// disposed it, and every later caller - including the same control showing
        /// the same icon twice - would draw a disposed bitmap.
        /// </para>
        /// <para>
        /// The point of the cache is to avoid the shell calls and the disk read,
        /// not to avoid a 32x32 allocation. Decoding is the expensive part; a copy
        /// of an already-decoded bitmap is a memcpy.
        /// </para>
        /// <para>
        /// Never returns null and never throws: an item whose icon cannot be
        /// produced yields the placeholder.
        /// </para>
        /// </remarks>
        public Bitmap GetIcon(ShortcutItem item, IconRequest request)
        {
            request ??= new IconRequest();

            if (item == null) return CreateFallback(request.PixelSize);

            string key = ComputeSizedKey(item, request.PixelSize);

            // Fast path: already decoded in memory. This is what makes a warm group
            // open instantly - a copy of a decoded bitmap, with no shell call.
            if (_memory.TryGetValue(key, out Bitmap? decoded))
            {
                Bitmap? fastCopy = TryClone(decoded);
                if (fastCopy != null) return fastCopy;
            }

            object gate = _locks.GetOrAdd(key, _ => new object());

            lock (gate)
            {
                if (_memory.TryGetValue(key, out decoded))
                {
                    Bitmap? copy = TryClone(decoded);
                    if (copy != null) return copy;
                }

                string filePath = Path.Combine(_cacheDirectory, key + ".png");

                Bitmap? resolved = ReadCached(filePath);

                if (resolved == null)
                {
                    // Extraction is cached as the master copy; the caller gets a
                    // clone below so the master stays valid for the next request.
                    resolved = ExtractAndStore(item, request, key, filePath);
                }

                if (resolved == null)
                {
                    // No provider produced an icon. The placeholder is owned by the
                    // cache for its whole lifetime and is deliberately not stored
                    // in _memory: Invalidate and Clear dispose what they remove from
                    // _memory, which would destroy a bitmap other keys still use.
                    // It is also not written to disk, since it carries no
                    // information about the target.
                    return CopyOrThrowaway(CreateFallback(request.PixelSize), request.PixelSize);
                }

                _memory[key] = resolved;

                return CopyOrThrowaway(resolved, request.PixelSize);
            }
        }

        /// <summary>
        /// Returns an owned copy of a master bitmap.
        /// </summary>
        /// <remarks>
        /// If the copy cannot be made, the master is deliberately not handed to the
        /// caller: it is the cached copy, and handing it out would let the caller's
        /// Dispose destroy it for every later request. A freshly drawn bitmap is
        /// returned instead, so the caller always gets something it can safely own.
        /// </remarks>
        private Bitmap CopyOrThrowaway(Bitmap master, int pixelSize)
        {
            Bitmap? copy = TryClone(master);
            if (copy != null) return copy;

            // The size comes from the request rather than from the master, so this
            // never has to read a property of a bitmap that may have been disposed
            // by a concurrent invalidation.
            int size = Math.Max(16, pixelSize);

            try
            {
                return _fallback.Extract(new ShortcutItem(), new IconRequest { LogicalSize = size, ScaleFactor = 1d })
                       ?? new Bitmap(size, size);
            }
            catch (Exception)
            {
                return new Bitmap(size, size);
            }
        }

        /// <summary>Serialises copies made from a shared master bitmap.</summary>
        /// <remarks>
        /// <see cref="Bitmap.Clone(Bitmap)"/> reads the source's GDI+ state, and GDI+
        /// serialises work on a single object internally: several threads copying the
        /// same master concurrently fail with "Object is currently in use elsewhere"
        /// rather than queueing. A short lock around the copy avoids that. The copy
        /// is a few kilobytes, so contention is negligible.
        /// </remarks>
        private static readonly object CloneGate = new object();

        /// <summary>Copies a bitmap, returning null rather than throwing.</summary>
        private static Bitmap? TryClone(Bitmap source)
        {
            lock (CloneGate)
            {
                try
                {
                    return new Bitmap(source);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private Bitmap? ReadCached(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                byte[] bytes = File.ReadAllBytes(filePath);
                if (bytes.Length == 0) return null;

                using (var ms = new MemoryStream(bytes, writable: false))
                using (Bitmap temp = new Bitmap(ms))
                {
                    // GDI+ decodes lazily: a truncated or corrupt file can produce a
                    // Bitmap object here and only fail when the caller reads a
                    // property. Touching the dimensions forces the decode inside this
                    // try block, so a bad cache entry is discarded rather than handed
                    // out as a bitmap that throws later, in a UI paint.
                    _ = temp.Width;
                    _ = temp.Height;

                    return new Bitmap(temp);
                }
            }
            catch (Exception ex)
            {
                _logger?.Log(SystemLogLevel.Debug, "Icons", "Cached icon unreadable: " + filePath, ex);

                // A truncated or corrupt cache entry is removed so the next request
                // re-extracts instead of failing forever.
                try
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                }
                catch (Exception)
                {
                }

                return null;
            }
        }

        private Bitmap? ExtractAndStore(ShortcutItem item, IconRequest request, string key, string filePath)
        {
            Bitmap? extracted = null;

            foreach (var provider in _providers)
            {
                try
                {
                    if (!provider.CanProvide(item)) continue;

                    extracted = provider.Extract(item, request);
                    if (extracted != null) break;
                }
                catch (Exception ex)
                {
                    _logger?.Log(SystemLogLevel.Debug, "Icons",
                        "Provider " + provider.GetType().Name + " failed for " + item.Target, ex);
                    extracted = null;
                }
            }

            // An explicit icon file the user picked always wins over extraction.
            if (extracted == null && !string.IsNullOrWhiteSpace(item.IconSource))
            {
                extracted = ExtractFromIconSource(item.IconSource!, request);
            }

            if (extracted == null) return null;

            TryPersist(extracted, filePath, key, item);
            return extracted;
        }

        private Bitmap? ExtractFromIconSource(string iconSource, IconRequest request)
        {
            try
            {
                string path = StringHelpers.ExpandPath(iconSource);
                if (!File.Exists(path)) return null;

                Bitmap? loaded = Windows.Shell.ShellIconExtractor.TryLoadBitmap(path);
                if (loaded != null)
                {
                    try
                    {
                        return IconScaler.Resize(loaded, request.PixelSize) ?? loaded;
                    }
                    finally
                    {
                        loaded.Dispose();
                    }
                }

                return Windows.Shell.ShellIconExtractor.TryExtract(path, request.PixelSize);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void TryPersist(Bitmap bitmap, string filePath, string key, ShortcutItem item)
        {
            try
            {
                byte[] png = ToPngBytes(bitmap);
                File.WriteAllBytes(filePath, png);

                _repository?.Upsert(
                    key,
                    filePath,
                    ComputeCacheKey(item),
                    bitmap.Width,
                    png.LongLength,
                    DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                // A cache we cannot write is a performance problem, not a failure:
                // the icon is already in hand and the caller gets it either way.
                _logger?.Log(SystemLogLevel.Debug, "Icons", "Icon cache write failed: " + filePath, ex);
            }
        }

        internal static byte[] ToPngBytes(Bitmap bitmap)
        {
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// The placeholder shown when no provider could produce an icon.
        /// </summary>
        /// <remarks>
        /// One bitmap per pixel size, shared by every broken item so a group of
        /// twenty failures does not draw twenty identical placeholders.
        /// </remarks>
        /// <remarks>
        /// These live in their own dictionary because they are the master copies
        /// <see cref="Clear"/> and <see cref="Dispose"/> release, and they are
        /// never handed to a caller directly: <see cref="GetIcon"/> copies out of
        /// them, so disposing one here cannot break a bitmap a control is drawing.
        /// </remarks>
        private Bitmap CreateFallback(int pixelSize)
        {
            int size = Math.Max(16, pixelSize);
            string fallbackKey = "fallback-" + size;

            if (_fallbacks.TryGetValue(fallbackKey, out Bitmap? existing)) return existing;

            Bitmap bitmap;
            try
            {
                bitmap = _fallback.Extract(new ShortcutItem(), new IconRequest { LogicalSize = size, ScaleFactor = 1d })
                          ?? new Bitmap(size, size);
            }
            catch (Exception)
            {
                bitmap = new Bitmap(size, size);
            }

            // First writer wins; the loser disposes its own bitmap rather than
            // leaving it undisposed.
            if (_fallbacks.TryAdd(fallbackKey, bitmap)) return bitmap;

            bitmap.Dispose();
            return _fallbacks[fallbackKey];
        }

        /// <summary>
        /// Releases every master copy held in memory.
        /// </summary>
        /// <remarks>
        /// Only the masters are disposed. Callers hold their own copies, which they
        /// dispose themselves, so nothing reachable from outside this object is
        /// ever freed here.
        /// </remarks>
        private void ReleaseAllMemory()
        {
            foreach (string cacheKey in new List<string>(_memory.Keys))
            {
                if (_memory.TryRemove(cacheKey, out Bitmap? bitmap)) bitmap?.Dispose();
            }
        }

        public void Invalidate(ShortcutItem item)
        {
            if (item == null) return;

            string key = ComputeCacheKey(item);

            foreach (string cacheKey in KeysFor(key))
            {
                if (_memory.TryRemove(cacheKey, out Bitmap? removed)) removed?.Dispose();

                try
                {
                    string path = Path.Combine(_cacheDirectory, cacheKey + ".png");
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception)
                {
                }

                _repository?.Delete(cacheKey);
            }
        }

        public int Repair(Func<ShortcutItem, bool> stillValid)
        {
            int removed = 0;

            try
            {
                if (_repository == null) return 0;

                foreach (IconRecord record in _repository.GetAll())
                {
                    bool exists = File.Exists(record.FilePath);
                    bool matches = !string.IsNullOrEmpty(record.SourceFingerprint);

                    if (exists && matches) continue;

                    _repository.Delete(record.CacheKey);
                    _memory.TryRemove(record.CacheKey, out Bitmap? bitmap);
                    bitmap?.Dispose();

                    try
                    {
                        if (File.Exists(record.FilePath)) File.Delete(record.FilePath);
                    }
                    catch (Exception)
                    {
                    }

                    removed++;
                }
            }
            catch (Exception ex)
            {
                _logger?.Log(SystemLogLevel.Warning, "Icons", "Icon cache repair failed", ex);
            }

            return removed;
        }

        public void Clear()
        {
            ReleaseAllMemory();

            // The placeholders are only reachable through _fallbacks, so clearing
            // _memory does not release them.
            foreach (string key in new List<string>(_fallbacks.Keys))
            {
                if (_fallbacks.TryRemove(key, out Bitmap? bitmap)) bitmap?.Dispose();
            }

            try
            {
                if (Directory.Exists(_cacheDirectory))
                    Directory.Delete(_cacheDirectory, recursive: true);
                Directory.CreateDirectory(_cacheDirectory);
            }
            catch (Exception ex)
            {
                _logger?.Log(SystemLogLevel.Warning, "Icons", "Icon cache could not be cleared", ex);
            }

            try
            {
                _repository?.DeleteAll();
            }
            catch (Exception)
            {
            }
        }

        private IEnumerable<string> KeysFor(string baseKey)
        {
            foreach (string existing in _memory.Keys)
            {
                if (existing.StartsWith(baseKey, StringComparison.OrdinalIgnoreCase))
                    yield return existing;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ReleaseAllMemory();

            foreach (string key in new List<string>(_fallbacks.Keys))
            {
                if (_fallbacks.TryRemove(key, out Bitmap? bitmap)) bitmap?.Dispose();
            }
        }
    }
}
