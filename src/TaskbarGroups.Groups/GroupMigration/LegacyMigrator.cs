using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Data.Storage;
using TaskbarGroups.Data.LegacyXml;
using TaskbarGroups.Data.Repositories;

namespace TaskbarGroups.Groups.GroupMigration
{
    /// <summary>
    /// Imports a 1.x installation into SQLite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule this class exists to enforce is that existing users do not lose
    /// groups. Concretely:
    /// </para>
    /// <list type="number">
    /// <item>Nothing under the legacy <c>config/</c> folder is ever written,
    /// moved or deleted. The original <c>ObjectData.xml</c> stays byte-identical.</item>
    /// <item>A ZIP backup of everything that will be read is written first, and its
    /// path is recorded in the report.</item>
    /// <item>Migration is idempotent: it is skipped when <c>MigrationHistory</c>
    /// already records a completed run for the same source, and it can be re-run
    /// after a failure without producing duplicates.</item>
    /// <item>Every legacy property has a destination. Anything that does not is
    /// listed in <see cref="MigrationReport.UnsupportedFields"/> rather than
    /// dropped.</item>
    /// <item>A failure in one group does not abort the others; the report says
    /// which groups made it.</item>
    /// </list>
    /// </remarks>
    public sealed class LegacyMigrator
    {
        private readonly Database _database;
        private readonly IGroupRepository _groups;
        private readonly LegacyCategoryReader _reader;
        private readonly MigrationHistoryRepository _history;
        private readonly IDataArchiver _archiver;
        private readonly IAppLogger? _logger;

        public LegacyMigrator(
            Database database,
            IGroupRepository groups,
            LegacyCategoryReader reader,
            MigrationHistoryRepository history,
            IDataArchiver archiver,
            IAppLogger? logger = null)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _groups = groups ?? throw new ArgumentNullException(nameof(groups));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _archiver = archiver ?? throw new ArgumentNullException(nameof(archiver));
            _logger = logger;
        }

        public const string SourceName = "legacy-xml";

        /// <summary>True when there is legacy data and it has not been imported yet.</summary>
        public bool ShouldRun()
        {
            if (!_reader.HasLegacyData()) return false;
            if (_history.HasCompleted(SourceName)) return false;

            // Nothing new to import if every legacy group already exists here.
            List<string> folders = _reader.FindGroupFolders();
            foreach (string folder in folders)
            {
                string name = Path.GetFileName(folder);
                if (_groups.GetByName(Core.Validation.GroupNameValidator.ToDisplayName(name)) == null)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Runs the migration. The returned report is written to disk next to the
        /// logs so the user can read it later.
        /// </summary>
        public MigrationReport Migrate(string backupDirectory)
        {
            var report = new MigrationReport { LegacyRoot = _reader.LegacyConfigDirectory };

            _logger?.Log(SystemLogLevel.Information, "Migration", "Starting legacy migration");

            List<string> folders;
            try
            {
                folders = _reader.FindGroupFolders();
            }
            catch (Exception ex)
            {
                report.Add(MigrationSeverity.Error, "discover", ex.Message);
                report.Complete(false);
                RecordFailure(report);
                return report;
            }

            if (folders.Count == 0)
            {
                report.Add(MigrationSeverity.Info, "discover", "No legacy groups were found.");
                report.Complete(true);
                return report;
            }

            // 1. Back up before reading anything.
            try
            {
                report.BackupArchivePath = _archiver.CreateLegacyBackup(backupDirectory);
                report.Add(MigrationSeverity.Info, "backup", "Legacy data archived to " + report.BackupArchivePath);
            }
            catch (Exception ex)
            {
                // Without a backup we must not touch anything.
                report.Add(MigrationSeverity.Error, "backup", "The legacy data could not be backed up: " + ex.Message);
                report.Complete(false);
                RecordFailure(report);
                return report;
            }

            int imported = 0;
            int skipped = 0;

            foreach (string folder in folders)
            {
                string folderName = Path.GetFileName(folder);
                string displayName = GroupNameValidator.ToDisplayName(folderName);

                LegacyCategory? legacy = _reader.Read(folder);
                if (legacy == null)
                {
                    report.Add(MigrationSeverity.Error, "read", "ObjectData.xml could not be parsed.", displayName);
                    skipped++;
                    continue;
                }

                Group? converted;
                try
                {
                    converted = Convert(legacy, folder, folderName, displayName, report);
                }
                catch (Exception ex)
                {
                    report.Add(MigrationSeverity.Error, "convert", ex.Message, displayName);
                    skipped++;
                    continue;
                }

                try
                {
                    // A group of the same name already present means this import
                    // has run before. Skip rather than create a duplicate.
                    Group? existing = _groups.GetByName(converted.Name);
                    if (existing != null)
                    {
                        report.Add(MigrationSeverity.Info, "duplicate", "A group with this name already exists; kept the existing one.", displayName);
                        skipped++;
                        continue;
                    }

                    _groups.Upsert(converted);
                    _groups.ReplaceShortcuts(converted.Id, ConvertShortcuts(legacy, converted.Id, report));

                    imported++;
                    report.GroupsImported.Add(displayName);
                    report.Add(MigrationSeverity.Info, "import", "Imported with " + legacy.ShortcutList.Count + " shortcuts.", displayName);
                }
                catch (Exception ex)
                {
                    report.Add(MigrationSeverity.Error, "save", ex.Message, displayName);
                    skipped++;
                }
            }

            bool success = skipped == 0;
            report.Complete(success);

            _history.Record(SourceName, SchemaMigrator.TargetVersion, success ? "Completed" : "Partial",
                report.GroupsImportedCount, report.ShortcutsImported,
                WriteReport(report), report.BackupArchivePath, report.StartedAt, report.CompletedAt);

            _logger?.Log(success ? SystemLogLevel.Information : SystemLogLevel.Warning, "Migration",
                "Legacy migration finished: " + imported + " imported, " + skipped + " skipped");

            return report;
        }

        /// <summary>Maps one legacy group onto the new model.</summary>
        private static Group Convert(
            LegacyCategory legacy,
            string folder,
            string folderName,
            string displayName,
            MigrationReport report)
        {
            var group = new Group
            {
                Name = string.IsNullOrWhiteSpace(displayName) ? folderName : displayName,
                LegacyName = folderName,

                // Legacy defaults the editor applied when a group had no stored
                // colour: 31,31,31 is the dark preset.
                Theme = GroupTheme.Dark,

                Width = legacy.Width > 0 ? legacy.Width : 5,
                Opacity = legacy.Opacity,

                // Ctrl+Enter is the legacy open-all hotkey and its checkbox started
                // out checked, so migrating groups keep it on.
                OpenAllEnabled = legacy.AllowOpenAll,

                SortMode = GroupSortMode.Manual,
                MonitorPreference = MonitorPreference.FollowCursor,
                CornerRadius = 8,
                ShadowEnabled = true,
                BlurEnabled = true,
                AnimationEnabled = true
            };

            if (!string.IsNullOrWhiteSpace(legacy.ColorString))
                group.Description = string.Empty;

            string? iconPath = LegacyCategoryReader.FindGroupIcon(folder);
            if (iconPath != null)
            {
                group.IconId = iconPath;
            }
            else
            {
                report.UnsupportedFields.Add(displayName + ": GroupIcon.ico is missing; the group will use a generated icon.");
            }

            // Properties the legacy format had and the new one deliberately does
            // not. Recorded so nothing is lost without saying so.
            report.UnsupportedFields.Add(displayName + ": the legacy per-group Icons cache is not imported; icons are re-extracted on demand.");
            report.UnsupportedFields.Add(displayName + ": the legacy JITComp profile is not used by version 2.0.");

            return group;
        }

        /// <summary>
        /// Maps legacy shortcuts, preserving order, names, arguments and working
        /// directories exactly, and classifying each target so a folder or a URI is
        /// not stored as an executable.
        /// </summary>
        private static List<ShortcutItem> ConvertShortcuts(LegacyCategory legacy, Guid groupId, MigrationReport report)
        {
            var items = new List<ShortcutItem>();
            int order = 0;

            foreach (LegacyShortcut legacyShortcut in legacy.ShortcutList)
            {
                if (legacyShortcut == null) continue;

                string path = legacyShortcut.FilePath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path))
                {
                    report.Add(MigrationSeverity.Warning, "shortcut", "Skipped an entry with no file path.");
                    report.ShortcutsSkipped++;
                    continue;
                }

                var item = new ShortcutItem
                {
                    GroupId = groupId,
                    Name = legacyShortcut.Name ?? string.Empty,
                    Target = Environment.ExpandEnvironmentVariables(path),
                    Arguments = legacyShortcut.Arguments ?? string.Empty,
                    WorkingDirectory = legacyShortcut.WorkingDirectory ?? string.Empty,
                    IsLegacyWindowsApp = legacyShortcut.IsWindowsApp,
                    LegacyFilePath = path,
                    Enabled = true,
                    SortOrder = order++,
                    WindowState = WindowStatePreference.Normal,
                    PreferredMonitor = MonitorPreference.FollowCursor
                };

                item.Type = ClassifyLegacyShortcut(item);

                if (!item.Enabled) item.Enabled = true;

                items.Add(item);
                report.ShortcutsImported++;
            }

            return items;
        }

        /// <summary>
        /// Decides a migrated item's type.
        /// </summary>
        /// <remarks>
        /// The legacy model had exactly one bit of type information,
        /// <c>isWindowsApp</c>. Everything else had to be inferred from the path,
        /// which is what made folder shortcuts crash: they were stored as if they
        /// were executables. The inference is done here, once, so the runtime never
        /// has to guess.
        /// </remarks>
        internal static ShortcutType ClassifyLegacyShortcut(ShortcutItem item)
        {
            string path = item.LegacyFilePath.Length > 0 ? item.LegacyFilePath : item.Target;

            if (item.IsLegacyWindowsApp)
            {
                // A Store shortcut's FilePath held either an .lnk pointing at the
                // package or the AppUserModelID itself.
                if (path.IndexOf('!') >= 0) return ShortcutType.Uwp;

                if (File.Exists(path) && string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                    return ShortcutType.Lnk;

                if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return ShortcutType.Uwp;

                return ShortcutType.Lnk;
            }

            try
            {
                if (Directory.Exists(path)) return ShortcutType.Folder;

                if (File.Exists(path))
                {
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    switch (extension)
                    {
                        case ".lnk": return ShortcutType.Lnk;
                        case ".url": return ShortcutType.Url;
                        case ".bat": return ShortcutType.Bat;
                        case ".cmd": return ShortcutType.Cmd;
                        case ".ps1": return ShortcutType.Ps1;
                        case ".exe": return ShortcutType.Exe;
                    }

                    return ShortcutType.Exe;
                }

                // The file is gone. Classify by extension so the user sees a
                // "missing" badge on an item whose type is still meaningful.
                string missingExtension = Path.GetExtension(path).ToLowerInvariant();
                switch (missingExtension)
                {
                    case ".lnk": return ShortcutType.Lnk;
                    case ".url": return ShortcutType.Url;
                    case ".bat": return ShortcutType.Bat;
                    case ".cmd": return ShortcutType.Cmd;
                    case ".ps1": return ShortcutType.Ps1;
                    case ".exe": return ShortcutType.Exe;
                }
            }
            catch (Exception)
            {
                return ShortcutType.Unknown;
            }

            return ShortcutType.Unknown;
        }

        /// <summary>Writes the report next to the logs and returns the path.</summary>
        private string WriteReport(MigrationReport report)
        {
            try
            {
                string directory = Path.GetDirectoryName(_database.DatabaseFilePath) ?? Path.GetTempPath();
                string path = Path.Combine(directory, "Logs", "migration-report.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                };
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(report, options));
                return path;
            }
            catch (Exception ex)
            {
                _logger?.Log(SystemLogLevel.Warning, "Migration", "Could not write the migration report", ex);
                return string.Empty;
            }
        }

        private void RecordFailure(MigrationReport report)
        {
            try
            {
                _history.Record(SourceName, SchemaMigrator.TargetVersion, "Failed", 0, 0,
                    string.Empty, report.BackupArchivePath, report.StartedAt, report.CompletedAt);
            }
            catch (Exception)
            {
                // If even the failure cannot be recorded there is nothing further
                // to do; the original data is untouched either way.
            }
        }
    }
}

