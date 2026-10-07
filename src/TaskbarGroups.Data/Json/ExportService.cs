using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskbarGroups.Core.Constants;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;

namespace TaskbarGroups.Data.Json
{
    /// <summary>
    /// Reads and writes the versioned JSON document used for export, import and
    /// backup.
    /// </summary>
    /// <remarks>
    /// Two properties matter more than the format itself:
    /// <list type="bullet">
    /// <item><b>Import is total.</b> An unreadable file, a future schema version,
    /// a malformed icon or a shortcut pointing at nothing each produce a recorded
    /// problem and the rest of the document still loads. A backup that can only
    /// be restored if it is perfectly formed is not a backup.</item>
    /// <item><b>Export never writes an absolute developer path.</b> Everything is
    /// recorded as the user configured it, and the file system layout is described
    /// relative to the data directory, so a backup taken on one machine restores
    /// cleanly on another.</item>
    /// </list>
    /// </remarks>
    public sealed class ExportService
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Imports a document, reporting everything that could not be used.</summary>
        public ImportResult Import(string json)
        {
            var result = new ImportResult();
            result.Document = new ExportDocument();

            if (string.IsNullOrWhiteSpace(json))
            {
                result.Problems.Add("The file is empty.");
                return result;
            }

            ExportDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<ExportDocument>(json, Options);
            }
            catch (JsonException ex)
            {
                result.Problems.Add("The file is not valid JSON: " + ex.Message);
                return result;
            }

            if (document == null)
            {
                result.Problems.Add("The file contains no Taskbar Groups document.");
                return result;
            }

            result.Document = document;

            if (document.SchemaVersion > ExportSchema.CurrentVersion)
            {
                result.Problems.Add(
                    "This file was written by a newer version (schema " + document.SchemaVersion +
                    ", this build supports " + ExportSchema.CurrentVersion + "). It was read as far as possible.");
            }

            if (document.Groups != null)
            {
                foreach (ExportGroup exported in document.Groups)
                {
                    if (exported == null) continue;

                    // A group with no name cannot be shown, found or renamed, so it is
                    // reported rather than imported as an unlabelled entry.
                    if (string.IsNullOrWhiteSpace(exported.Name))
                    {
                        result.Problems.Add("A group in the file has no name and was not imported.");
                        continue;
                    }

                    result.Groups.Add(exported.ToDomain());
                }
            }

            if (document.Workspaces != null)
            {
                foreach (ExportWorkspace exported in document.Workspaces)
                {
                    if (exported == null) continue;
                    result.Workspaces.Add(exported.ToDomain());
                }
            }

            result.Settings = document.Settings;
            result.Themes = document.Themes ?? new List<ThemeDefinition>();

            // Report rather than silently drop: the requirement is that an
            // unsupported property is never discarded quietly. Notes are kept
            // separate from problems so a routine "icons are not embedded" note
            // does not make a successful import read as a failure.
            if (document.Notes != null)
            {
                foreach (string note in document.Notes)
                {
                    if (!string.IsNullOrWhiteSpace(note)) result.Notes.Add(note);
                }
            }

            return result;
        }

        /// <summary>Reads a document from disk.</summary>
        public ImportResult ImportFile(string path)
        {
            try
            {
                return Import(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                var result = new ImportResult();
                result.Problems.Add("The file could not be read: " + ex.Message);
                return result;
            }
        }

        /// <summary>Serialises a document to JSON.</summary>
        public string Export(ExportDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            document.SchemaVersion = ExportSchema.CurrentVersion;
            if (string.IsNullOrWhiteSpace(document.ApplicationVersion))
                document.ApplicationVersion = CurrentApplicationVersion();

            return JsonSerializer.Serialize(document, Options);
        }

        /// <summary>Writes a document to disk, creating the folder if needed.</summary>
        public string ExportFile(ExportDocument document, string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory!);

            File.WriteAllText(path, Export(document));
            return path;
        }

        public static string CurrentApplicationVersion()
        {
            try
            {
                System.Reflection.AssemblyName? name =
                    System.Reflection.Assembly.GetEntryAssembly()?.GetName();
                return name?.Version?.ToString() ?? "2.0.0";
            }
            catch (Exception)
            {
                return "2.0.0";
            }
        }
    }

    /// <summary>Outcome of an import.</summary>
    public sealed class ImportResult
    {
        public ImportResult()
        {
            Groups = new List<Group>();
            Workspaces = new List<Workspace>();
            Themes = new List<ThemeDefinition>();
            Problems = new List<string>();
        }

        public ExportDocument Document { get; set; } = new ExportDocument();

        public List<Group> Groups { get; }

        public List<Workspace> Workspaces { get; }

        public List<ThemeDefinition> Themes { get; set; }

        public AppSettings? Settings { get; set; }

        /// <summary>Every problem found, for display. Never fatal on its own.</summary>
        public List<string> Problems { get; }

        /// <summary>
        /// Informational notes carried by the document, kept apart from
        /// <see cref="Problems"/> so a healthy import does not look like a failed one.
        /// </summary>
        public List<string> Notes { get; } = new List<string>();

        public bool HasContent => Groups.Count > 0 || Workspaces.Count > 0;

        /// <summary>Shortcuts in the imported groups, with their group applied.</summary>
        public List<ShortcutItem> BuildShortcuts()
        {
            var items = new List<ShortcutItem>();

            foreach (Group group in Groups)
            {
                ExportGroup? exported = Document.Groups?.Find(g => g.Id == group.Id);
                if (exported == null) continue;

                int order = 0;
                foreach (ExportShortcut shortcut in exported.Shortcuts)
                {
                    ShortcutItem item = shortcut.ToDomain(group.Id);
                    item.SortOrder = shortcut.SortOrder != 0 ? shortcut.SortOrder : order;

                    ValidationResult validation = ShortcutValidator.Validate(item);
                    if (!validation.IsValid && validation.Message != null)
                        Problems.Add(group.Name + " / " + item.Name + ": " + validation.Message);

                    items.Add(item);
                    order++;
                }
            }

            return items;
        }
    }
}
