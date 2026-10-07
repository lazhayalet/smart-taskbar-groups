using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace TaskbarGroups.Data.LegacyXml
{
    /// <summary>
    /// The 1.x ObjectData.xml document, verbatim.
    /// </summary>
    /// <remarks>
    /// This type exists only to read the old format. It is deliberately a separate
    /// shape from the new domain model: the legacy file is an
    /// <see cref="XmlSerializer"/> contract over public fields on
    /// <c>client.Classes.Category</c>, and reshaping it would change what
    /// serialises. Keeping it frozen means an old file can always be read, even
    /// after the application has moved on.
    ///
    /// Unknown elements are ignored by the deserialiser rather than rejected, which
    /// is what allows a file written by a newer 1.x patch to be read.
    /// </remarks>
    [XmlRoot("Category")]
    public sealed class LegacyCategory
    {
        public LegacyCategory()
        {
            ShortcutList = new List<LegacyShortcut>();
        }

        /// <summary>Folder-form name: spaces replaced by underscores.</summary>
        [XmlElement("Name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>ColorTranslator.ToHtml output, e.g. #1f1f1f. Absent in old releases.</summary>
        [XmlElement("ColorString")]
        public string? ColorString { get; set; }

        /// <summary>Legacy open-all flag.</summary>
        [XmlElement("allowOpenAll")]
        public bool AllowOpenAll { get; set; }

        /// <summary>Shortcuts per row.</summary>
        [XmlElement("Width")]
        public int Width { get; set; }

        /// <summary>0 = solid, 100 = fully transparent.</summary>
        [XmlElement("Opacity")]
        public double Opacity { get; set; } = 10d;

        [XmlArray("ShortcutList")]
        [XmlArrayItem("ProgramShortcut")]
        public List<LegacyShortcut> ShortcutList { get; set; }
    }

    /// <summary>One 1.x shortcut record.</summary>
    /// <remarks>
    /// <c>FilePath</c> and <c>isWindowsApp</c> were auto-properties and
    /// <c>Arguments</c>/<c>WorkingDirectory</c> were public fields in the legacy
    /// class. The XML serialiser emits both forms identically, so the reader
    /// accepts either by taking the value that is present.
    /// </remarks>
    public sealed class LegacyShortcut
    {
        public LegacyShortcut()
        {
            Arguments = string.Empty;
            WorkingDirectory = string.Empty;
        }

        [XmlElement("FilePath")]
        public string FilePath { get; set; } = string.Empty;

        [XmlElement("isWindowsApp")]
        public bool IsWindowsApp { get; set; }

        [XmlElement("name")]
        public string Name { get; set; } = string.Empty;

        [XmlElement("Arguments")]
        public string Arguments { get; set; }

        [XmlElement("WorkingDirectory")]
        public string WorkingDirectory { get; set; }
    }

    /// <summary>Reads legacy group folders without modifying them.</summary>
    /// <remarks>
    /// The legacy layout is <c>&lt;data&gt;/config/&lt;Group_Name&gt;/ObjectData.xml</c>,
    /// alongside <c>GroupIcon.ico</c>, <c>GroupImage.png</c> and an
    /// <c>Icons/</c> cache folder.
    ///
    /// Nothing in this class writes to, moves or deletes the legacy tree. The
    /// migration writes to SQLite and leaves the original files exactly where they
    /// were, so a user who wants to go back to 1.x still can.
    /// </remarks>
    public sealed class LegacyCategoryReader
    {
        private readonly string _legacyConfigDirectory;

        public LegacyCategoryReader(string legacyConfigDirectory)
        {
            if (string.IsNullOrWhiteSpace(legacyConfigDirectory))
                throw new ArgumentException("A legacy config directory is required.", nameof(legacyConfigDirectory));

            _legacyConfigDirectory = legacyConfigDirectory;
        }

        public string LegacyConfigDirectory => _legacyConfigDirectory;

        /// <summary>True when there is at least one group folder to migrate.</summary>
        public bool HasLegacyData()
        {
            try
            {
                return Directory.Exists(_legacyConfigDirectory)
                       && Directory.GetDirectories(_legacyConfigDirectory).Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Every group folder that actually contains an ObjectData.xml.</summary>
        public List<string> FindGroupFolders()
        {
            var folders = new List<string>();

            try
            {
                if (!Directory.Exists(_legacyConfigDirectory)) return folders;

                foreach (string directory in Directory.GetDirectories(_legacyConfigDirectory))
                {
                    if (File.Exists(Path.Combine(directory, "ObjectData.xml")))
                        folders.Add(directory);
                }
            }
            catch (Exception)
            {
                // A folder we cannot read is not a group we can migrate; the
                // migration report records the count difference.
            }

            folders.Sort(StringComparer.OrdinalIgnoreCase);
            return folders;
        }

        /// <summary>Reads one group folder. Returns null when the file is unreadable.</summary>
        public LegacyCategory? Read(string groupFolder)
        {
            string path = Path.Combine(groupFolder, "ObjectData.xml");
            if (!File.Exists(path)) return null;

            try
            {
                // XmlReaderSettings with a small expansion limit: the file is
                // user data, but a DTD in it should not be able to exhaust memory.
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreWhitespace = true
                };

                using (XmlReader reader = XmlReader.Create(path, settings))
                {
                    var serializer = new XmlSerializer(typeof(LegacyCategory));
                    return serializer.Deserialize(reader) as LegacyCategory;
                }
            }
            catch (Exception)
            {
                // A malformed file yields null; the caller records it and moves on
                // rather than failing the whole migration.
                return null;
            }
        }

        /// <summary>Path of a group's 256px PNG image, when present.</summary>
        public static string? FindGroupImage(string groupFolder)
        {
            return FindExisting(groupFolder, "GroupImage.png");
        }

        /// <summary>Path of a group's multi-resolution icon, when present.</summary>
        public static string? FindGroupIcon(string groupFolder)
        {
            return FindExisting(groupFolder, "GroupIcon.ico");
        }

        /// <summary>Path of a group's legacy per-shortcut icon cache folder.</summary>
        public static string? FindIconCache(string groupFolder)
        {
            string path = Path.Combine(groupFolder, "Icons");
            return Directory.Exists(path) ? path : null;
        }

        private static string? FindExisting(string folder, string fileName)
        {
            try
            {
                string path = Path.Combine(folder, fileName);
                return File.Exists(path) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}