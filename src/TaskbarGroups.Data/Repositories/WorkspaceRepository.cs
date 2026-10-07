using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;

using TaskbarGroups.Data.Storage;

namespace TaskbarGroups.Data.Repositories
{
    /// <summary>SQLite-backed workspace persistence.</summary>
    public sealed class WorkspaceRepository : IWorkspaceRepository
    {
        private readonly Database _database;

        public WorkspaceRepository(Database database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public List<Workspace> GetAll()
        {
            var workspaces = new List<Workspace>();

            using SqliteCommand command = _database.CreateCommand(
                "SELECT Id, Name, Description, IconId, LaunchDelayMs, CreatedAt, UpdatedAt FROM Workspaces ORDER BY Name COLLATE NOCASE;");

            using SqliteDataReader reader = command.ExecuteReader();
            var ids = new List<Guid>();

            while (reader.Read())
            {
                Workspace workspace = ReadWorkspace(reader);
                workspaces.Add(workspace);
                ids.Add(workspace.Id);
            }

            // Items are loaded in one query for the whole set rather than one
            // query per workspace; the dashboard lists them all at once.
            foreach (Workspace workspace in workspaces)
                workspace.Items = GetItems(workspace.Id);

            return workspaces;
        }

        public Workspace? GetById(Guid id)
        {
            if (id == Guid.Empty) return null;

            using SqliteCommand command = _database.CreateCommand(
                "SELECT Id, Name, Description, IconId, LaunchDelayMs, CreatedAt, UpdatedAt FROM Workspaces WHERE Id = $id;",
                ("$id", id.ToString()));

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return null;

            Workspace workspace = ReadWorkspace(reader);
            workspace.Items = GetItems(workspace.Id);
            return workspace;
        }

        public Workspace? GetByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            using SqliteCommand command = _database.CreateCommand(
                "SELECT Id, Name, Description, IconId, LaunchDelayMs, CreatedAt, UpdatedAt FROM Workspaces WHERE Name = $name COLLATE NOCASE;",
                ("$name", name.Trim()));

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return null;

            Workspace workspace = ReadWorkspace(reader);
            workspace.Items = GetItems(workspace.Id);
            return workspace;
        }

        public void Upsert(Workspace workspace)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            if (workspace.Id == Guid.Empty) workspace.Id = Guid.NewGuid();
            if (string.IsNullOrWhiteSpace(workspace.Name)) throw new ArgumentException("A workspace needs a name.", nameof(workspace));

            workspace.Touch();

            _database.InTransaction(transaction =>
            {
                using (SqliteCommand command = _database.Connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO Workspaces (Id, Name, Description, IconId, LaunchDelayMs, CreatedAt, UpdatedAt)
VALUES ($id, $name, $description, $iconId, $delay, $created, $updated)
ON CONFLICT(Id) DO UPDATE SET
    Name = excluded.Name,
    Description = excluded.Description,
    IconId = excluded.IconId,
    LaunchDelayMs = excluded.LaunchDelayMs,
    UpdatedAt = excluded.UpdatedAt;";

                    SqlParameterBinder.Add(command, "$id", workspace.Id.ToString());
                    SqlParameterBinder.Add(command, "$name", workspace.Name.Trim());
                    SqlParameterBinder.Add(command, "$description", SqlValue.Text(workspace.Description));
                    SqlParameterBinder.Add(command, "$iconId", SqlValue.Text(workspace.IconId));
                    SqlParameterBinder.Add(command, "$delay", workspace.LaunchDelayMs);
                    SqlParameterBinder.Add(command, "$created", SqlValue.Time(workspace.CreatedAt));
                    SqlParameterBinder.Add(command, "$updated", SqlValue.Time(workspace.UpdatedAt));
                    command.ExecuteNonQuery();
                }

                using (SqliteCommand delete = _database.Connection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM WorkspaceItems WHERE WorkspaceId = $id;";
                    SqlParameterBinder.Add(delete, "$id", workspace.Id.ToString());
                    delete.ExecuteNonQuery();
                }

                int order = 0;
                foreach (WorkspaceItem item in workspace.Items)
                {
                    if (item == null) continue;
                    item.WorkspaceId = workspace.Id;
                    if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
                    if (item.SortOrder == 0) item.SortOrder = order;

                    using SqliteCommand insert = _database.Connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = @"
INSERT INTO WorkspaceItems (Id, WorkspaceId, ProcessMatch, Executable, MonitorDeviceName,
                             X, Y, Width, Height, WindowState, NormalizedPlacement,
                             LaunchDelayMs, SortOrder)
VALUES ($id, $ws, $match, $exe, $monitor, $x, $y, $w, $h, $state, $anchor, $delay, $sort);";

                    SqlParameterBinder.Add(insert, "$id", item.Id.ToString());
                    SqlParameterBinder.Add(insert, "$ws", workspace.Id.ToString());
                    SqlParameterBinder.Add(insert, "$match", SqlValue.Text(item.ProcessMatch));
                    SqlParameterBinder.Add(insert, "$exe", SqlValue.Text(item.Executable));
                    SqlParameterBinder.Add(insert, "$monitor", SqlValue.Text(item.MonitorDeviceName));
                    SqlParameterBinder.Add(insert, "$x", item.X);
                    SqlParameterBinder.Add(insert, "$y", item.Y);
                    SqlParameterBinder.Add(insert, "$w", item.Width);
                    SqlParameterBinder.Add(insert, "$h", item.Height);
                    SqlParameterBinder.Add(insert, "$state", SqlValue.EnumText(item.WindowState));
                    SqlParameterBinder.Add(insert, "$anchor", SqlValue.EnumText(item.NormalizedPlacement));
                    SqlParameterBinder.Add(insert, "$delay", item.LaunchDelayMs);
                    SqlParameterBinder.Add(insert, "$sort", item.SortOrder);
                    insert.ExecuteNonQuery();

                    order++;
                }

                return true;
            });
        }

        public void Delete(Guid id)
        {
            if (id == Guid.Empty) return;

            _database.InTransaction(transaction =>
            {
                using SqliteCommand command = _database.Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM Workspaces WHERE Id = $id;";
                SqlParameterBinder.Add(command, "$id", id.ToString());
                command.ExecuteNonQuery();
                return true;
            });
        }

        public List<WindowPlacement> GetPlacements(Guid workspaceId)
        {
            var placements = new List<WindowPlacement>();
            if (workspaceId == Guid.Empty) return placements;

            using SqliteCommand command = _database.CreateCommand(
                "SELECT Id, WorkspaceId, ProcessMatch, Executable, MonitorId, X, Y, Width, Height, WindowState, CapturedAt " +
                "FROM WindowPlacements WHERE WorkspaceId = $id;",
                ("$id", workspaceId.ToString()));

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                placements.Add(ReadPlacement(reader));

            return placements;
        }

        public void ReplacePlacements(Guid workspaceId, IEnumerable<WindowPlacement> placements)
        {
            var list = new List<WindowPlacement>();
            if (placements != null)
            {
                foreach (WindowPlacement placement in placements)
                {
                    if (placement == null) continue;
                    if (placement.Id == Guid.Empty) placement.Id = Guid.NewGuid();
                    placement.WorkspaceId = workspaceId;
                    list.Add(placement);
                }
            }

            _database.InTransaction(transaction =>
            {
                using (SqliteCommand delete = _database.Connection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM WindowPlacements WHERE WorkspaceId = $id;";
                    SqlParameterBinder.Add(delete, "$id", workspaceId.ToString());
                    delete.ExecuteNonQuery();
                }

                foreach (WindowPlacement placement in list)
                {
                    using SqliteCommand insert = _database.Connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = @"
INSERT INTO WindowPlacements (Id, WorkspaceId, ProcessMatch, Executable, MonitorId,
                               X, Y, Width, Height, WindowState, CapturedAt)
VALUES ($id, $ws, $match, $exe, $monitor, $x, $y, $w, $h, $state, $captured);";

                    SqlParameterBinder.Add(insert, "$id", placement.Id.ToString());
                    SqlParameterBinder.Add(insert, "$ws", workspaceId.ToString());
                    SqlParameterBinder.Add(insert, "$match", SqlValue.Text(placement.ProcessMatch));
                    SqlParameterBinder.Add(insert, "$exe", SqlValue.Text(placement.Executable));
                    SqlParameterBinder.Add(insert, "$monitor", SqlValue.Text(placement.MonitorId));
                    SqlParameterBinder.Add(insert, "$x", placement.X);
                    SqlParameterBinder.Add(insert, "$y", placement.Y);
                    SqlParameterBinder.Add(insert, "$w", placement.Width);
                    SqlParameterBinder.Add(insert, "$h", placement.Height);
                    SqlParameterBinder.Add(insert, "$state", SqlValue.EnumText(placement.WindowState));
                    SqlParameterBinder.Add(insert, "$captured", SqlValue.Time(placement.CapturedAt));
                    insert.ExecuteNonQuery();
                }

                return true;
            });
        }

        private List<WorkspaceItem> GetItems(Guid workspaceId)
        {
            var items = new List<WorkspaceItem>();

            using SqliteCommand command = _database.CreateCommand(
                "SELECT Id, WorkspaceId, ProcessMatch, Executable, MonitorDeviceName, X, Y, Width, Height, " +
                "WindowState, NormalizedPlacement, LaunchDelayMs, SortOrder " +
                "FROM WorkspaceItems WHERE WorkspaceId = $id ORDER BY SortOrder;",
                ("$id", workspaceId.ToString()));

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) items.Add(ReadItem(reader));

            return items;
        }

        private static Workspace ReadWorkspace(IDataRecord record)
        {
            return new Workspace
            {
                Id = record.GetGuid("Id"),
                Name = record.GetString("Name"),
                Description = record.GetString("Description"),
                IconId = record.GetString("IconId"),
                LaunchDelayMs = record.GetInt("LaunchDelayMs", 750),
                CreatedAt = record.GetTime("CreatedAt"),
                UpdatedAt = record.GetTime("UpdatedAt")
            };
        }

        private static WorkspaceItem ReadItem(IDataRecord record)
        {
            return new WorkspaceItem
            {
                Id = record.GetGuid("Id"),
                WorkspaceId = record.GetGuid("WorkspaceId"),
                ProcessMatch = record.GetString("ProcessMatch"),
                Executable = record.GetString("Executable"),
                MonitorDeviceName = record.GetString("MonitorDeviceName"),
                X = record.GetInt("X"),
                Y = record.GetInt("Y"),
                Width = record.GetInt("Width"),
                Height = record.GetInt("Height"),
                WindowState = record.GetEnum("WindowState", WindowStatePreference.Normal),
                NormalizedPlacement = record.GetEnum("NormalizedPlacement", PlacementAnchor.Center),
                LaunchDelayMs = record.GetInt("LaunchDelayMs"),
                SortOrder = record.GetInt("SortOrder")
            };
        }

        private static WindowPlacement ReadPlacement(IDataRecord record)
        {
            return new WindowPlacement
            {
                Id = record.GetGuid("Id"),
                WorkspaceId = record.GetGuid("WorkspaceId"),
                ProcessMatch = record.GetString("ProcessMatch"),
                Executable = record.GetString("Executable"),
                MonitorId = record.GetString("MonitorId"),
                X = record.GetInt("X"),
                Y = record.GetInt("Y"),
                Width = record.GetInt("Width", 800),
                Height = record.GetInt("Height", 600),
                WindowState = record.GetEnum("WindowState", WindowStatePreference.Normal),
                CapturedAt = record.GetTime("CapturedAt")
            };
        }
    }

    /// <summary>Key/value settings stored as JSON documents.</summary>
    public sealed class SettingsRepository
    {
        public const string AppSettingsKey = "app-settings";
        public const string ThemesKey = "themes";

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly Database _database;

        public SettingsRepository(Database database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public T Load<T>(string key, Func<T> fallback) where T : class
        {
            try
            {
                using SqliteCommand command = _database.CreateCommand("SELECT Value FROM Settings WHERE Key = $key;", ("$key", key));
                object? value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value) return fallback();

                string json = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                if (json.Length == 0) return fallback();

                return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? fallback();
            }
            catch (JsonException)
            {
                // A settings file from a newer build, or hand-edited. The defaults
                // are better than refusing to start.
                return fallback();
            }
            catch (Exception)
            {
                return fallback();
            }
        }

        public void Save<T>(string key, T value)
        {
            string json = JsonSerializer.Serialize(value, SerializerOptions);

            _database.InTransaction(transaction =>
            {
                using SqliteCommand command = _database.Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO Settings (Key, Value) VALUES ($key, $value)
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
                SqlParameterBinder.Add(command, "$key", key);
                SqlParameterBinder.Add(command, "$value", json);
                command.ExecuteNonQuery();
                return true;
            });
        }

        public static JsonSerializerOptions CreateSerializerOptions()
        {
            return new JsonSerializerOptions(SerializerOptions);
        }
    }

    /// <summary>Icon cache metadata.</summary>
    public sealed class IconRepository : IIconRepository
    {
        private readonly Database _database;

        public IconRepository(Database database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public void Upsert(string cacheKey, string filePath, string sourceFingerprint, int pixelSize, long byteLength, DateTimeOffset createdAt)
        {
            if (string.IsNullOrWhiteSpace(cacheKey)) return;

            _database.InTransaction(transaction =>
            {
                using SqliteCommand command = _database.Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO Icons (CacheKey, FilePath, SourceFingerprint, PixelSize, ByteLength, CreatedAt)
VALUES ($key, $path, $fingerprint, $size, $bytes, $created)
ON CONFLICT(CacheKey) DO UPDATE SET
    FilePath = excluded.FilePath,
    SourceFingerprint = excluded.SourceFingerprint,
    PixelSize = excluded.PixelSize,
    ByteLength = excluded.ByteLength,
    CreatedAt = excluded.CreatedAt;";
                SqlParameterBinder.Add(command, "$key", cacheKey);
                SqlParameterBinder.Add(command, "$path", filePath ?? string.Empty);
                SqlParameterBinder.Add(command, "$fingerprint", sourceFingerprint ?? string.Empty);
                SqlParameterBinder.Add(command, "$size", pixelSize);
                SqlParameterBinder.Add(command, "$bytes", byteLength);
                SqlParameterBinder.Add(command, "$created", SqlValue.Time(createdAt));
                command.ExecuteNonQuery();
                return true;
            });
        }

        public IconRecord? Get(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey)) return null;

            using SqliteCommand command = _database.CreateCommand(
                "SELECT CacheKey, FilePath, SourceFingerprint, PixelSize, ByteLength, CreatedAt FROM Icons WHERE CacheKey = $key;",
                ("$key", cacheKey));

            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }

        public IEnumerable<IconRecord> GetAll()
        {
            var records = new List<IconRecord>();

            using SqliteCommand command = _database.CreateCommand(
                "SELECT CacheKey, FilePath, SourceFingerprint, PixelSize, ByteLength, CreatedAt FROM Icons;");

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) records.Add(Read(reader));

            return records;
        }

        public int Delete(string cacheKey)
        {
            using SqliteCommand command = _database.CreateCommand(
                "DELETE FROM Icons WHERE CacheKey = $key;", ("$key", cacheKey ?? string.Empty));
            return command.ExecuteNonQuery();
        }

        public int DeleteAll()
        {
            using SqliteCommand command = _database.CreateCommand("DELETE FROM Icons;");
            return command.ExecuteNonQuery();
        }

        private static IconRecord Read(IDataRecord record)
        {
            return new IconRecord
            {
                CacheKey = record.GetString("CacheKey"),
                FilePath = record.GetString("FilePath"),
                SourceFingerprint = record.GetString("SourceFingerprint"),
                PixelSize = record.GetInt("PixelSize", 32),
                ByteLength = record.GetInt("ByteLength"),
                CreatedAt = record.GetTime("CreatedAt")
            };
        }
    }

    /// <summary>The migration log, so a re-run is never needed and a failure is traceable.</summary>
    public sealed class MigrationHistoryRepository
    {
        private readonly Database _database;

        public MigrationHistoryRepository(Database database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public void Record(string source, int version, string status, int groupsCount, int itemCount, string reportPath, string backupPath, DateTimeOffset startedAt, DateTimeOffset? completedAt)
        {
            _database.InTransaction(transaction =>
            {
                using SqliteCommand command = _database.Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO MigrationHistory (Source, Version, Status, GroupsCount, ItemCount, ReportPath, BackupPath, StartedAt, CompletedAt)
VALUES ($source, $version, $status, $groups, $items, $report, $backup, $started, $completed);";
                SqlParameterBinder.Add(command, "$source", source ?? string.Empty);
                SqlParameterBinder.Add(command, "$version", version);
                SqlParameterBinder.Add(command, "$status", status ?? string.Empty);
                SqlParameterBinder.Add(command, "$groups", groupsCount);
                SqlParameterBinder.Add(command, "$items", itemCount);
                SqlParameterBinder.Add(command, "$report", reportPath ?? string.Empty);
                SqlParameterBinder.Add(command, "$backup", backupPath ?? string.Empty);
                SqlParameterBinder.Add(command, "$started", SqlValue.Time(startedAt));
                SqlParameterBinder.AddOptionalTime(command, "$completed", completedAt);
                command.ExecuteNonQuery();
                return true;
            });
        }

        public bool HasCompleted(string source)
        {
            using SqliteCommand command = _database.CreateCommand(
                "SELECT COUNT(*) FROM MigrationHistory WHERE Source = $source AND Status = 'Completed';",
                ("$source", source ?? string.Empty));

            return SqlValue.ToInt(command.ExecuteScalar()) > 0;
        }

        public IEnumerable<(DateTimeOffset StartedAt, string Status, int Groups, string BackupPath)> GetAll()
        {
            var results = new List<(DateTimeOffset, string, int, string)>();

            using SqliteCommand command = _database.CreateCommand(
                "SELECT StartedAt, Status, GroupsCount, BackupPath FROM MigrationHistory ORDER BY Id DESC;");

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add((
                    reader.GetTime("StartedAt"),
                    reader.GetString("Status"),
                    reader.GetInt("GroupsCount"),
                    reader.GetString("BackupPath")));
            }

            return results;
        }
    }
}