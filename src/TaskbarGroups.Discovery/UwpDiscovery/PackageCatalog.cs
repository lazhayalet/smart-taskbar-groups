using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TaskbarGroups.Discovery.UwpDiscovery
{
    /// <summary>
    /// Enumerates packaged applications.
    /// </summary>
    /// <remarks>
    /// Uses only what a non-administrative desktop process may use: the package
    /// deployment API for the list of installed packages, and each package's own
    /// <c>AppxManifest.xml</c> for its identity and display name. Reading files out
    /// of <c>WindowsApps</c> directly is not attempted, because the folder is not
    /// readable by a normal process and guessing at paths is what broke the legacy
    /// icon lookup.
    /// </remarks>
    internal static class PackageCatalog
    {
        /// <summary>Package family names registered for the current user.</summary>
        internal static IEnumerable<string> EnumeratePackageFamilies()
        {
            var families = new List<string>();

            try
            {
                var manager = new global::Windows.Management.Deployment.PackageManager();
                IEnumerable<global::Windows.ApplicationModel.Package> packages = manager.FindPackages();

                foreach (var package in packages)
                {
                    string? name = package?.Id?.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (families.Contains(name!, StringComparer.OrdinalIgnoreCase)) continue;
                    families.Add(name!);
                }
            }
            catch (Exception)
            {
                // Deployment service unavailable in this session.
            }

            return families;
        }

        /// <summary>Packages belonging to a family, with an install location.</summary>
        internal static IEnumerable<global::Windows.ApplicationModel.Package> GetPackages(string familyName)
        {
            var results = new List<global::Windows.ApplicationModel.Package>();

            try
            {
                var manager = new global::Windows.Management.Deployment.PackageManager();
                IEnumerable<global::Windows.ApplicationModel.Package> packages =
                    manager.FindPackagesForUser(string.Empty, familyName);

                foreach (var package in packages)
                {
                    if (package == null) continue;
                    if (string.IsNullOrWhiteSpace(package.InstalledLocation?.Path)) continue;
                    results.Add(package);
                }
            }
            catch (Exception)
            {
            }

            return results;
        }

        /// <summary>
        /// The application's AppUserModelID, built from the manifest.
        /// </summary>
        /// <remarks>
        /// The ID is <c>&lt;package family name&gt;!&lt;application Id&gt;</c> and
        /// is what <c>shell:appsFolder</c> needs, so it is assembled rather than
        /// guessed. A package with several applications yields the first, which is
        /// the one the Start Menu shows.
        /// </remarks>
        internal static string? ReadApplicationId(string installLocation)
        {
            var manifest = LoadManifest(installLocation);
            if (manifest == null) return null;

            XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

            XElement? application = manifest.Root
                ?.Element(ns + "Applications")
                ?.Elements(ns + "Application")
                .FirstOrDefault();

            // Attribute() returns an XAttribute, so the identity element is held in
            // a local before its Name attribute is read.
            XElement? identity = manifest.Root?.Element(XName.Get("Identity", ns.NamespaceName));

            string? appId = application?.Attribute(XName.Get("Id", ""))?.Value;
            string? familyName = identity?.Attribute(XName.Get("Name", ""))?.Value;

            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(familyName))
                return null;

            return familyName + "!" + appId;
        }

        internal static string? ReadDisplayName(string installLocation)
        {
            var manifest = LoadManifest(installLocation);
            if (manifest == null) return null;

            XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
            XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

            XElement? visual = manifest.Root
                ?.Element(ns + "Applications")
                ?.Elements(ns + "Application")
                .Select(a => a.Element(uap + "VisualElements"))
                .FirstOrDefault(e => e != null);

            string? name = visual?.Attribute(XName.Get("DisplayName", ""))?.Value;
            if (!string.IsNullOrWhiteSpace(name)) return name;

            return manifest.Root?.Element(ns + "Properties")?.Attribute(XName.Get("DisplayName", ""))?.Value;
        }

        /// <summary>Path of a package's best logo asset, or null.</summary>
        internal static string? ReadLogoPath(string installLocation, int preferredSize = 200)
        {
            var manifest = LoadManifest(installLocation);
            if (manifest == null) return null;

            XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

            string? logo = manifest.Root?.Element(ns + "Properties")?.Element(ns + "Logo")?.Value;
            if (string.IsNullOrWhiteSpace(logo)) return null;

            string folder = Path.Combine(installLocation, logo!.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                if (!Directory.Exists(folder)) return null;

                string scale = "scale-" + preferredSize;
                FileInfo[] matching = new DirectoryInfo(folder).GetFiles("*" + scale + "*.*");
                if (matching.Length > 0) return matching[0].FullName;

                FileInfo[] storeLogos = new DirectoryInfo(folder).GetFiles("*StoreLogo*.*");
                if (storeLogos.Length > 0) return storeLogos[0].FullName;

                FileInfo[] any = new DirectoryInfo(folder).GetFiles("*.png");
                return any.Length > 0 ? any[0].FullName : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static XDocument? LoadManifest(string installLocation)
        {
            try
            {
                string path = Path.Combine(installLocation, "AppxManifest.xml");
                if (!File.Exists(path)) return null;
                return XDocument.Load(path);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}