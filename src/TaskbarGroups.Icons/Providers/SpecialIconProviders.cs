using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Windows.Shell;

namespace TaskbarGroups.Icons.Providers
{
    /// <summary>
    /// Icon of a packaged (Store) application, read from its installed package.
    /// </summary>
    /// <remarks>
    /// Replaces <c>handleWindowsApp.getWindowsAppIcon</c>. The original:
    /// called <c>packages.First()</c> on a sequence that is empty when the package
    /// is not installed for the current user, throwing <c>InvalidOperationException</c>;
    /// built the install path from <c>%ProgramW6432%</c>, which is unset on a 32-bit
    /// shell and gives a wrong path on a machine with a relocated WindowsApps;
    /// called <c>XmlDocument.Load("" + "\\AppxManifest.xml")</c> when the lookup
    /// failed; dereferenced the result of <c>Icon.ExtractAssociatedIcon</c> without
    /// a null check; and did all of it synchronously on the UI thread.
    ///
    /// This version is total: it returns null at every step where the legacy code
    /// would have thrown, caches the parsed manifest, and resolves the package
    /// folder from the package object rather than from an environment variable.
    /// </remarks>
    public sealed class UwpIconProvider : IconProviderBase
    {
        private readonly Dictionary<string, PackageLogo?> _logoCache =
            new Dictionary<string, PackageLogo?>(StringComparer.OrdinalIgnoreCase);

        private readonly object _gate = new object();

        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get
            {
                yield return ShortcutType.Uwp;
                yield return ShortcutType.Msix;
            }
        }

        public override bool CanProvide(ShortcutItem item)
        {
            return item != null && (item.Type == ShortcutType.Uwp || item.Type == ShortcutType.Msix);
        }

        protected override Bitmap? ExtractCore(ShortcutItem item, int pixelSize)
        {
            string appId = NormalizeAppId(item.Target);
            if (appId.Length == 0) return null;

            PackageLogo? logo = FindLogo(appId);
            if (logo == null) return null;

            Bitmap? bitmap = ShellIconExtractor.TryLoadBitmap(logo.Path);
            if (bitmap == null) return null;

            try
            {
                return IconScaler.Resize(bitmap, pixelSize);
            }
            finally
            {
                bitmap.Dispose();
            }
        }

        /// <summary>Display name of a packaged application, or the id when it cannot be read.</summary>
        public string GetDisplayName(string appUserModelId)
        {
            string familyName = NormalizeAppId(appUserModelId);
            int bang = familyName.IndexOf('!');
            if (bang > 0) familyName = familyName.Substring(0, bang);

            PackageLogo? logo = FindLogo(familyName);
            if (logo != null && !string.IsNullOrWhiteSpace(logo.DisplayName))
                return logo.DisplayName;

            return familyName;
        }

        private PackageLogo? FindLogo(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return null;

            lock (_gate)
            {
                if (_logoCache.TryGetValue(familyName, out PackageLogo? cached))
                    return cached;
            }

            PackageLogo? result = null;
            try
            {
                result = QueryLogo(familyName);
            }
            catch (Exception)
            {
                // No package enumeration on this machine, or access denied. The
                // caller falls back to another provider.
                result = null;
            }

            lock (_gate)
            {
                _logoCache[familyName] = result;
            }

            return result;
        }

        private static PackageLogo? QueryLogo(string familyName)
        {
            var packages = PackageCatalog.GetPackagesForFamily(familyName);
            if (packages == null) return null;

            foreach (string installLocation in packages)
            {
                if (string.IsNullOrWhiteSpace(installLocation)) continue;

                string manifestPath = Path.Combine(installLocation, "AppxManifest.xml");
                if (!File.Exists(manifestPath)) continue;

                string? logoRelative = ReadLogoRelativePath(manifestPath);
                if (string.IsNullOrWhiteSpace(logoRelative)) continue;

                string logoFolder = Path.Combine(installLocation, logoRelative!);
                string? logoFile = PickBestLogo(logoFolder);
                if (logoFile == null) continue;

                return new PackageLogo
                {
                    Path = logoFile,
                    DisplayName = ReadDisplayName(manifestPath) ?? familyName
                };
            }

            return null;
        }

        private static string? ReadLogoRelativePath(string manifestPath)
        {
            try
            {
                XDocument document = XDocument.Load(manifestPath);
                XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

                XElement? properties = document.Root?.Element(ns + "Properties");
                XElement? logo = properties?.Element(ns + "Logo");
                string? value = logo?.Value;

                if (string.IsNullOrWhiteSpace(value)) return null;
                return value!.Replace('/', Path.DirectorySeparatorChar);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string? ReadDisplayName(string manifestPath)
        {
            try
            {
                XDocument document = XDocument.Load(manifestPath);
                XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
                XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

                XElement? visual = document.Root
                    ?.Element(ns + "Applications")
                    ?.Elements(ns + "Application")
                    .Select(a => a.Element(uap + "VisualElements"))
                    .FirstOrDefault(e => e != null);

                string? visualName = visual?.Attribute("DisplayName")?.Value;
                if (!string.IsNullOrWhiteSpace(visualName)) return visualName;

                return document.Root?.Element(ns + "Properties")?.Attribute("DisplayName")?.Value;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Chooses the best logo in a package's logo folder. StoreLogo assets are
        /// square and legible at any size, so they are preferred; scale-200 is the
        /// fallback.
        /// </summary>
        private static string? PickBestLogo(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return null;

                FileInfo[] storeLogos = new DirectoryInfo(folder).GetFiles("*StoreLogo*.*");
                if (storeLogos.Length > 0)
                {
                    return storeLogos.OrderByDescending(f => f.Length).First().FullName;
                }

                FileInfo[] scaled = new DirectoryInfo(folder).GetFiles("*scale-200*.*");
                if (scaled.Length > 0)
                {
                    return scaled[0].FullName;
                }

                FileInfo[] any = new DirectoryInfo(folder).GetFiles("*.png");
                return any.Length > 0 ? any[0].FullName : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static string NormalizeAppId(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            string value = raw!.Trim();
            const string appsFolder = "shell:appsFolder\\";
            if (value.StartsWith(appsFolder, StringComparison.OrdinalIgnoreCase))
                return value.Substring(appsFolder.Length).Trim();

            if (value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                return value.Substring("shell:".Length).Trim();

            return value;
        }

        private sealed class PackageLogo
        {
            public string Path { get; set; } = string.Empty;

            public string DisplayName { get; set; } = string.Empty;
        }
    }

    /// <summary>
    /// Package enumeration, isolated so the WinRT type does not leak into
    /// signatures and so every failure mode has one place to be handled.
    /// </summary>
    internal static class PackageCatalog
    {
        /// <summary>
        /// Install locations of the packages in a family. The legacy code did this
        /// through <c>global::Windows.Management.Deployment.PackageManager</c> and then
        /// rebuilt the path from <c>%ProgramW6432%\WindowsApps\</c>, which is wrong
        /// on a 32-bit process and on a machine with a relocated store. The package
        /// object reports its own location, so no assumption is needed.
        /// </summary>
        internal static IEnumerable<string> GetPackagesForFamily(string familyName)
        {
            var results = new List<string>();

            try
            {
                var manager = new global::Windows.Management.Deployment.PackageManager();
                IEnumerable<global::Windows.ApplicationModel.Package> packages =
                    manager.FindPackagesForUser(string.Empty, familyName);

                if (packages == null) return results;

                foreach (var package in packages)
                {
                    if (package == null) continue;

                    // Only a package with an install location can contribute a
                    // logo. A package that is staged, tampered with or not
                    // installed for this user has none, and asking for one is
                    // what the legacy First() call assumed it could do.
                    string? location = package.InstalledLocation?.Path;
                    if (string.IsNullOrWhiteSpace(location)) continue;
                    if (!Directory.Exists(location)) continue;

                    results.Add(location!);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // The current user cannot enumerate this package family.
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // The deployment service is unavailable, e.g. in a restricted session.
            }
            catch (Exception)
            {
            }

            return results;
        }
    }

    /// <summary>Icon of an installed browser progressive web app.</summary>
    public sealed class PwaIconProvider : IconProviderBase
    {
        private readonly Func<string, string?> _iconLocator;

        public PwaIconProvider(Func<string, string?> iconLocator)
        {
            _iconLocator = iconLocator ?? throw new ArgumentNullException(nameof(iconLocator));
        }

        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get { yield return ShortcutType.Pwa; }
        }

        public override bool CanProvide(ShortcutItem item)
        {
            return item != null && item.Type == ShortcutType.Pwa && !string.IsNullOrWhiteSpace(item.Target);
        }

        protected override Bitmap? ExtractCore(ShortcutItem item, int pixelSize)
        {
            string? iconPath = _iconLocator(item.Target);
            if (string.IsNullOrWhiteSpace(iconPath)) return null;

            if (!File.Exists(iconPath)) return null;

            string extension = Path.GetExtension(iconPath).ToLowerInvariant();
            if (extension == ".ico") return ShellIconExtractor.TryExtractFromIco(iconPath, pixelSize);

            Bitmap? bitmap = ShellIconExtractor.TryLoadBitmap(iconPath);
            if (bitmap == null) return null;

            try
            {
                return IconScaler.Resize(bitmap, pixelSize);
            }
            finally
            {
                bitmap.Dispose();
            }
        }
    }

    /// <summary>Icon of a Steam game, taken from the client's grid artwork.</summary>
    public sealed class SteamIconProvider : IconProviderBase
    {
        private readonly SteamArtLookup _art;

        public SteamIconProvider(SteamArtLookup art)
        {
            _art = art ?? throw new ArgumentNullException(nameof(art));
        }

        public override IEnumerable<ShortcutType> SupportedTypes
        {
            get { yield return ShortcutType.Steam; }
        }

        public override bool CanProvide(ShortcutItem item)
        {
            return item != null && item.Type == ShortcutType.Steam;
        }

        protected override Bitmap? ExtractCore(ShortcutItem item, int pixelSize)
        {
            string? iconPath = _art.FindIcon(item.Target);
            if (string.IsNullOrWhiteSpace(iconPath)) return null;

            if (!File.Exists(iconPath)) return null;

            if (Path.GetExtension(iconPath).Equals(".ico", StringComparison.OrdinalIgnoreCase))
                return ShellIconExtractor.TryExtractFromIco(iconPath, pixelSize);

            Bitmap? bitmap = ShellIconExtractor.TryLoadBitmap(iconPath);
            if (bitmap == null) return null;

            try
            {
                return IconScaler.Resize(bitmap, pixelSize);
            }
            finally
            {
                bitmap.Dispose();
            }
        }
    }

    /// <summary>Finds artwork for a Steam title.</summary>
    public sealed class SteamArtLookup
    {
        private readonly string _steamRoot;

        public SteamArtLookup(string steamRoot)
        {
            _steamRoot = steamRoot ?? string.Empty;
        }

        public string? FindIcon(string steamUri)
        {
            if (string.IsNullOrWhiteSpace(_steamRoot) || string.IsNullOrWhiteSpace(steamUri)) return null;

            string? appId = ExtractAppId(steamUri);
            if (appId == null) return null;

            string libraryFolder = Path.Combine(_steamRoot, "appcache", "librarycache");
            if (!Directory.Exists(libraryFolder)) return null;

            try
            {
                // Grid artwork is stored under _<appid>_<page>.ico.
                string[] candidates = Directory.GetFiles(libraryFolder, "_" + appId + "_*.ico", SearchOption.AllDirectories);
                if (candidates.Length > 0)
                    return Array.Find(candidates, c => c.IndexOf("_grid_", StringComparison.OrdinalIgnoreCase) >= 0)
                           ?? candidates[0];
            }
            catch (Exception)
            {
            }

            return null;
        }

        public static string? ExtractAppId(string steamUri)
        {
            const string marker = "rungameid/";
            int index = steamUri.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;

            string tail = steamUri.Substring(index + marker.Length);
            int end = tail.IndexOfAny(new[] { '/', '?', '&', ' ' });
            if (end >= 0) tail = tail.Substring(0, end);

            return tail.Trim().Length > 0 ? tail.Trim() : null;
        }
    }
}