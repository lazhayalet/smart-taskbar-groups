using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using TaskbarGroups.Core.Interfaces;

namespace TaskbarGroups.Data.Storage
{
    /// <summary>
    /// Owns the SQLite connection and the schema.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single connection is shared rather than pooled. This is a desktop
    /// application with one user and one writer, so a pool would add a failure
    /// mode (two connections writing at once) without adding throughput.
    /// WAL journaling is on, foreign keys are enforced, and every schema change
    /// runs inside a transaction, so an interrupted migration leaves the previous
    /// schema intact.
    /// </para>
    /// </remarks>
    public sealed class Database : IDisposable
    {
        private readonly string _connectionString;
        private readonly object _gate = new object();
        private SqliteConnection? _connection;
        private bool _disposed;

        public Database(string databaseFilePath)
        {
            if (string.IsNullOrWhiteSpace(databaseFilePath))
                throw new ArgumentException("A database file path is required.", nameof(databaseFilePath));

            DatabaseFilePath = databaseFilePath;

            string? directory = System.IO.Path.GetDirectoryName(databaseFilePath);
            if (!string.IsNullOrEmpty(directory))
                System.IO.Directory.CreateDirectory(directory!);

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databaseFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                ForeignKeys = true,
                Pooling = false
            }.ToString();
        }

        public string DatabaseFilePath { get; }

        /// <summary>Schema version currently present in the file.</summary>
        public int SchemaVersion
        {
            get
            {
                try
                {
                    using SqliteCommand command = CreateCommand("PRAGMA user_version;");
                    object? value = command.ExecuteScalar();
                    return value == null ? 0 : Convert.ToInt32(value);
                }
                catch (Exception)
                {
                    return 0;
                }
            }
        }

        public SqliteConnection Connection
        {
            get
            {
                lock (_gate)
                {
                    if (_disposed) throw new ObjectDisposedException(nameof(Database));

                    if (_connection == null)
                    {
                        _connection = new SqliteConnection(_connectionString);
                        _connection.Open();
                        ApplyPragmas(_connection);
                    }

                    return _connection;
                }
            }
        }

        private static void ApplyPragmas(SqliteConnection connection)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                // WAL keeps a long read (the icon scan) from blocking a write.
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
                command.ExecuteNonQuery();
            }
        }

        public SqliteCommand CreateCommand(string sql, params (string Name, object? Value)[] parameters)
        {
            var command = Connection.CreateCommand();
            command.CommandText = sql;

            foreach ((string name, object? value) in parameters)
            {
                SqliteParameter parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }

            return command;
        }

        /// <summary>Runs a unit of work in a transaction, rolling back on failure.</summary>
        public T InTransaction<T>(Func<SqliteTransaction, T> work)
        {
            lock (_gate)
            {
                using SqliteTransaction transaction = Connection.BeginTransaction();
                try
                {
                    T result = work(transaction);
                    transaction.Commit();
                    return result;
                }
                catch (Exception)
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception)
                    {
                        // Already rolled back.
                    }
                    throw;
                }
            }
        }

        public void Execute(string sql)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        /// <summary>Reads a single value, or default when there is no row.</summary>
        public T? Scalar<T>(string sql, params (string Name, object? Value)[] parameters)
        {
            using SqliteCommand command = CreateCommand(sql, parameters);
            object? value = command.ExecuteScalar();
            if (value == null || value == DBNull.Value) return default;
            return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>True when the database opens and answers a trivial query.</summary>
        public bool IsHealthy(out string error)
        {
            error = string.Empty;
            try
            {
                using SqliteCommand command = CreateCommand("SELECT 1;");
                object? value = command.ExecuteScalar();
                return Convert.ToInt32(value) == 1;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Runs the SQLite integrity check. Reports the first problem found.</summary>
        public bool CheckIntegrity(out string error)
        {
            error = string.Empty;
            try
            {
                using SqliteCommand command = CreateCommand("PRAGMA integrity_check;");
                object? value = command.ExecuteScalar();
                string? result = value as string;

                if (result != null && !result.Equals("ok", StringComparison.OrdinalIgnoreCase))
                {
                    error = result;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Vacuum and checkpoint. Used by the diagnostics "repair" action.</summary>
        public bool Repair(out string error)
        {
            error = string.Empty;
            try
            {
                Execute("PRAGMA wal_checkpoint(TRUNCATE);");
                Execute("VACUUM;");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Closes the connection after making sure nothing is half-written.</summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;

                try
                {
                    Execute("PRAGMA wal_checkpoint(TRUNCATE);");
                }
                catch (Exception)
                {
                }

                _connection?.Dispose();
                _connection = null;
            }
        }
    }

    /// <summary>Schema creation and versioned migrations.</summary>
    public static class SchemaMigrator
    {
        /// <summary>Version this build writes.</summary>
        public const int TargetVersion = 1;

        /// <summary>Creates the schema if it is missing and returns the resulting version.</summary>
        public static int Initialize(Database database, TaskbarGroups.Core.Interfaces.IAppLogger? logger = null)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));

            int current = database.SchemaVersion;

            if (current >= TargetVersion) return current;

            try
            {
                database.InTransaction(transaction =>
                {
                    Execute(database, transaction, CreateTables);
                    database.Execute("PRAGMA user_version = " + TargetVersion + ";");
                    return true;
                });

                logger?.Log(TaskbarGroups.Core.Interfaces.SystemLogLevel.Information, "Database",
                    "Schema created at version " + TargetVersion);

                return TargetVersion;
            }
            catch (Exception ex)
            {
                logger?.Log(TaskbarGroups.Core.Interfaces.SystemLogLevel.Error, "Database", "Schema creation failed", ex);
                throw;
            }
        }

        private static void Execute(Database database, SqliteTransaction? transaction, string sql)
        {
            using SqliteCommand command = database.Connection.CreateCommand();
            command.CommandText = sql;
            if (transaction != null) command.Transaction = transaction;
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// The whole schema in one string.
        /// </summary>
        /// <remarks>
        /// Written out rather than migrated step by step because this is the first
        /// versioned schema; <see cref="TargetVersion"/> and the versioned
        /// <c>PRAGMA user_version</c> are in place so later changes are additive.
        /// Indexes cover the access patterns the UI actually has: listing groups,
        /// listing a group's shortcuts in order, looking up a shortcut by target
        /// during validation, and looking up settings by key.
        /// </remarks>
        private const string CreateTables = @"
CREATE TABLE IF NOT EXISTS Groups (
    Id                        TEXT    NOT NULL PRIMARY KEY,
    Name                      TEXT    NOT NULL,
    Description               TEXT    NOT NULL DEFAULT '',
    IconId                    TEXT    NOT NULL DEFAULT '',
    Width                     INTEGER NOT NULL DEFAULT 5,
    Theme                     TEXT    NOT NULL DEFAULT 'System',
    Opacity                   REAL    NOT NULL DEFAULT 10,
    CornerRadius              INTEGER NOT NULL DEFAULT 8,
    ShadowEnabled             INTEGER NOT NULL DEFAULT 1,
    BlurEnabled               INTEGER NOT NULL DEFAULT 1,
    AnimationEnabled          INTEGER NOT NULL DEFAULT 1,
    OpenAllEnabled            INTEGER NOT NULL DEFAULT 1,
    SortMode                  TEXT    NOT NULL DEFAULT 'Manual',
    MonitorPreference         TEXT    NOT NULL DEFAULT 'FollowCursor',
    PreferredMonitorDeviceName TEXT  NOT NULL DEFAULT '',
    CreatedAt                 TEXT    NOT NULL,
    UpdatedAt                 TEXT    NOT NULL,
    LegacyName                TEXT    NOT NULL DEFAULT '',
    Origin                    TEXT    NOT NULL DEFAULT 'Manual',
    SortIndex                 INTEGER NOT NULL DEFAULT 0
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_Groups_Name ON Groups(Name COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS IX_Groups_SortIndex ON Groups(SortIndex);

CREATE TABLE IF NOT EXISTS ShortcutItems (
    Id                        TEXT    NOT NULL PRIMARY KEY,
    GroupId                   TEXT    NOT NULL,
    Name                      TEXT    NOT NULL DEFAULT '',
    Type                      TEXT    NOT NULL DEFAULT 'Unknown',
    Target                    TEXT    NOT NULL DEFAULT '',
    Arguments                 TEXT    NOT NULL DEFAULT '',
    WorkingDirectory          TEXT    NOT NULL DEFAULT '',
    IconSource                TEXT    NOT NULL DEFAULT '',
    IconCacheKey              TEXT    NOT NULL DEFAULT '',
    RunAsAdministrator        INTEGER NOT NULL DEFAULT 0,
    WindowState               TEXT    NOT NULL DEFAULT 'Normal',
    PreferredMonitor          TEXT    NOT NULL DEFAULT 'FollowCursor',
    PreferredMonitorDeviceName TEXT   NOT NULL DEFAULT '',
    Enabled                   INTEGER NOT NULL DEFAULT 1,
    SortOrder                 INTEGER NOT NULL DEFAULT 0,
    CreatedAt                 TEXT    NOT NULL,
    UpdatedAt                 TEXT    NOT NULL,
    IsLegacyWindowsApp        INTEGER NOT NULL DEFAULT 0,
    LegacyFilePath            TEXT    NOT NULL DEFAULT '',
    IsAutoDetected            INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (GroupId) REFERENCES Groups(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_ShortcutItems_Group ON ShortcutItems(GroupId, SortOrder);
CREATE INDEX IF NOT EXISTS IX_ShortcutItems_Target ON ShortcutItems(Target);

CREATE TABLE IF NOT EXISTS Workspaces (
    Id            TEXT    NOT NULL PRIMARY KEY,
    Name          TEXT    NOT NULL,
    Description   TEXT    NOT NULL DEFAULT '',
    IconId        TEXT    NOT NULL DEFAULT '',
    LaunchDelayMs INTEGER NOT NULL DEFAULT 750,
    CreatedAt     TEXT    NOT NULL,
    UpdatedAt     TEXT    NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_Workspaces_Name ON Workspaces(Name COLLATE NOCASE);

CREATE TABLE IF NOT EXISTS WorkspaceItems (
    Id                  TEXT    NOT NULL PRIMARY KEY,
    WorkspaceId         TEXT    NOT NULL,
    ProcessMatch        TEXT    NOT NULL DEFAULT '',
    Executable          TEXT    NOT NULL DEFAULT '',
    MonitorDeviceName   TEXT    NOT NULL DEFAULT '',
    X                   INTEGER NOT NULL DEFAULT 0,
    Y                   INTEGER NOT NULL DEFAULT 0,
    Width               INTEGER NOT NULL DEFAULT 0,
    Height              INTEGER NOT NULL DEFAULT 0,
    WindowState         TEXT    NOT NULL DEFAULT 'Normal',
    NormalizedPlacement TEXT    NOT NULL DEFAULT 'Center',
    LaunchDelayMs       INTEGER NOT NULL DEFAULT 0,
    SortOrder           INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (WorkspaceId) REFERENCES Workspaces(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_WorkspaceItems_Workspace ON WorkspaceItems(WorkspaceId, SortOrder);

CREATE TABLE IF NOT EXISTS WindowPlacements (
    Id           TEXT    NOT NULL PRIMARY KEY,
    WorkspaceId  TEXT    NULL,
    ProcessMatch TEXT    NOT NULL DEFAULT '',
    Executable   TEXT    NOT NULL DEFAULT '',
    MonitorId    TEXT    NOT NULL DEFAULT '',
    X            INTEGER NOT NULL DEFAULT 0,
    Y            INTEGER NOT NULL DEFAULT 0,
    Width        INTEGER NOT NULL DEFAULT 800,
    Height       INTEGER NOT NULL DEFAULT 600,
    WindowState  TEXT    NOT NULL DEFAULT 'Normal',
    CapturedAt   TEXT    NOT NULL,
    FOREIGN KEY (WorkspaceId) REFERENCES Workspaces(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_WindowPlacements_Workspace ON WindowPlacements(WorkspaceId);

CREATE TABLE IF NOT EXISTS Icons (
    CacheKey          TEXT    NOT NULL PRIMARY KEY,
    FilePath          TEXT    NOT NULL,
    SourceFingerprint TEXT    NOT NULL DEFAULT '',
    PixelSize         INTEGER NOT NULL DEFAULT 32,
    ByteLength        INTEGER NOT NULL DEFAULT 0,
    CreatedAt         TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS Settings (
    Key   TEXT NOT NULL PRIMARY KEY,
    Value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Themes (
    Name  TEXT NOT NULL PRIMARY KEY,
    Value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS MigrationHistory (
    Id          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Source      TEXT    NOT NULL,
    Version     INTEGER NOT NULL,
    Status      TEXT    NOT NULL,
    GroupsCount INTEGER NOT NULL DEFAULT 0,
    ItemCount   INTEGER NOT NULL DEFAULT 0,
    ReportPath  TEXT    NOT NULL DEFAULT '',
    BackupPath  TEXT    NOT NULL DEFAULT '',
    StartedAt   TEXT    NOT NULL,
    CompletedAt TEXT    NULL
);

CREATE INDEX IF NOT EXISTS IX_MigrationHistory_Source ON MigrationHistory(Source);
";
    }
}