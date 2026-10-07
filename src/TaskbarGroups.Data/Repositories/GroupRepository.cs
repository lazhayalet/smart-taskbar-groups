using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;

using TaskbarGroups.Data.Storage;

namespace TaskbarGroups.Data.Repositories
{
    /// <summary>
    /// Helpers for reading and writing the SQLite rows.
    /// </summary>
    /// <remarks>
    /// Enums, booleans and timestamps are stored as text rather than as integers.
    /// That costs a few bytes and buys three things: an export of the raw table is
    /// readable without the schema, an unexpected value round-trips through
    /// <c>Enum.TryParse</c> instead of throwing, and a timestamp survives in a
    /// form SQLite itself understands.
    /// </remarks>
    internal static class SqlValue
    {
        internal static string Text(string? value) => value ?? string.Empty;

        internal static string Bool(bool value) => value ? "1" : "0";

        internal static bool ToBool(object? value)
        {
            if (value == null || value == DBNull.Value) return false;
            if (value is bool b) return b;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
            return text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        internal static string Time(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        internal static DateTimeOffset ToTime(object? value)
        {
            if (value == null || value == DBNull.Value) return DateTimeOffset.UtcNow;

            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
            {
                return parsed;
            }

            return DateTimeOffset.UtcNow;
        }

        internal static DateTimeOffset? ToNullableTime(object? value)
        {
            if (value == null || value == DBNull.Value) return null;
            return ToTime(value);
        }


        internal static string EnumText<TEnum>(TEnum value) where TEnum : struct, Enum
        {
            return value.ToString();
        }

        internal static TEnum ToEnum<TEnum>(object? value, TEnum fallback) where TEnum : struct, Enum
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (text.Length == 0) return fallback;
            return Enum.TryParse(text, ignoreCase: true, out TEnum parsed) ? parsed : fallback;
        }

        internal static Guid ToGuid(object? value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return Guid.TryParse(text, out Guid parsed) ? parsed : Guid.Empty;
        }

        internal static int ToInt(object? value, int fallback = 0)
        {
            if (value == null || value == DBNull.Value) return fallback;
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        internal static double ToDouble(object? value, double fallback = 0d)
        {
            if (value == null || value == DBNull.Value) return fallback;
            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        internal static string GetString(this IDataRecord record, string column)
        {
            int index = record.GetOrdinal(column);
            return index < 0 || record.IsDBNull(index) ? string.Empty : Convert.ToString(record.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        internal static bool GetBool(this IDataRecord record, string column)
        {
            int index = record.GetOrdinal(column);
            return index >= 0 && ToBool(index < record.FieldCount ? record.GetValue(index) : null);
        }

        internal static int GetInt(this IDataRecord record, string column, int fallback = 0)
        {
            int index = record.GetOrdinal(column);
            return index < 0 ? fallback : ToInt(record.GetValue(index), fallback);
        }

        internal static double GetDouble(this IDataRecord record, string column, double fallback = 0d)
        {
            int index = record.GetOrdinal(column);
            return index < 0 ? fallback : ToDouble(record.GetValue(index), fallback);
        }

        internal static DateTimeOffset GetTime(this IDataRecord record, string column)
        {
            int index = record.GetOrdinal(column);
            return index < 0 ? DateTimeOffset.UtcNow : ToTime(record.GetValue(index));
        }

        internal static TEnum GetEnum<TEnum>(this IDataRecord record, string column, TEnum fallback) where TEnum : struct, Enum
        {
            int index = record.GetOrdinal(column);
            return index < 0 ? fallback : ToEnum(record.GetValue(index), fallback);
        }

        internal static Guid GetGuid(this IDataRecord record, string column)
        {
            int index = record.GetOrdinal(column);
            return index < 0 ? Guid.Empty : ToGuid(record.GetValue(index));
        }
    }

    /// <summary>SQLite-backed group persistence.</summary>
    public sealed class GroupRepository : IGroupRepository
    {
        private readonly Database _database;

        public GroupRepository(Database database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public List<Group> GetAll()
        {
            var groups = new List<Group>();

            using SqliteCommand command = _database.CreateCommand(
                "SELECT Id, Name, Description, IconId, Width, Theme, Opacity, CornerRadius, ShadowEnabled, " +
                "BlurEnabled, AnimationEnabled, OpenAllEnabled, SortMode, MonitorPreference, " +
                "PreferredMonitorDeviceName, CreatedAt, UpdatedAt, LegacyName " +
                "FROM Groups ORDER BY SortIndex, Name COLLATE NOCASE;");

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                groups.Add(ReadGroup(reader));

            return groups;
        }

        public Group? GetById(Guid id)
        {
            if (id == Guid.Empty) return null;

            using SqliteCommand command = _database.CreateCommand(
                SelectGroup + " WHERE Id = $id;",
                ("$id", id.ToString()));

            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadGroup(reader) : null;
        }

        public Group? GetByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            using SqliteCommand command = _database.CreateCommand(
                SelectGroup + " WHERE Name = $name COLLATE NOCASE;",
                ("$name", name.Trim()));

            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadGroup(reader) : null;
        }

        public void Upsert(Group group)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (group.Id == Guid.Empty) group.Id = Guid.NewGuid();
            if (string.IsNullOrWhiteSpace(group.Name)) throw new ArgumentException("A group needs a name.", nameof(group));

            group.Touch();

            int sortIndex = 0;
            using (SqliteCommand count = _database.CreateCommand("SELECT COUNT(*) FROM Groups;"))
            {
                sortIndex = SqlValue.ToInt(count.ExecuteScalar());
            }

            _database.InTransaction(transaction =>
            {
                using SqliteCommand command = _database.Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO Groups (Id, Name, Description, IconId, Width, Theme, Opacity, CornerRadius, ShadowEnabled,
                     BlurEnabled, AnimationEnabled, OpenAllEnabled, SortMode, MonitorPreference,
                     PreferredMonitorDeviceName, CreatedAt, UpdatedAt, LegacyName, Origin, SortIndex)
VALUES ($id, $name, $description, $iconId, $width, $theme, $opacity, $cornerRadius, $shadow, $blur,
        $animation, $openAll, $sortMode, $monitorPref, $monitorName, $created, $updated, $legacyName, $origin, $sortIndex)
ON CONFLICT(Id) DO UPDATE SET
    Name = excluded.Name,
    Description = excluded.Description,
    IconId = excluded.IconId,
    Width = excluded.Width,
    Theme = excluded.Theme,
    Opacity = excluded.Opacity,
    CornerRadius = excluded.CornerRadius,
    ShadowEnabled = excluded.ShadowEnabled,
    BlurEnabled = excluded.BlurEnabled,
    AnimationEnabled = excluded.AnimationEnabled,
    OpenAllEnabled = excluded.OpenAllEnabled,
    SortMode = excluded.SortMode,
    MonitorPreference = excluded.MonitorPreference,
    PreferredMonitorDeviceName = excluded.PreferredMonitorDeviceName,
    UpdatedAt = excluded.UpdatedAt,
    LegacyName = excluded.LegacyName;";

                SqlParameterBinder.Add(command, "$id", group.Id.ToString());
                SqlParameterBinder.Add(command, "$name", group.Name.Trim());
                SqlParameterBinder.Add(command, "$description", SqlValue.Text(group.Description));
                SqlParameterBinder.Add(command, "$iconId", SqlValue.Text(group.IconId));
                SqlParameterBinder.Add(command, "$width", group.Width);
                SqlParameterBinder.Add(command, "$theme", SqlValue.EnumText(group.Theme));
                SqlParameterBinder.Add(command, "$opacity", group.Opacity);
                SqlParameterBinder.Add(command, "$cornerRadius", group.CornerRadius);
                SqlParameterBinder.Add(command, "$shadow", SqlValue.Bool(group.ShadowEnabled));
                SqlParameterBinder.Add(command, "$blur", SqlValue.Bool(group.BlurEnabled));
                SqlParameterBinder.Add(command, "$animation", SqlValue.Bool(group.AnimationEnabled));
                SqlParameterBinder.Add(command, "$openAll", SqlValue.Bool(group.OpenAllEnabled));
                SqlParameterBinder.Add(command, "$sortMode", SqlValue.EnumText(group.SortMode));
                SqlParameterBinder.Add(command, "$monitorPref", SqlValue.EnumText(group.MonitorPreference));
                SqlParameterBinder.Add(command, "$monitorName", SqlValue.Text(group.PreferredMonitorDeviceName));
                SqlParameterBinder.Add(command, "$created", SqlValue.Time(group.CreatedAt));
                SqlParameterBinder.Add(command, "$updated", SqlValue.Time(group.UpdatedAt));
                SqlParameterBinder.Add(command, "$legacyName", SqlValue.Text(group.LegacyName));
                SqlParameterBinder.Add(command, "$origin", SqlValue.EnumText(GroupOrigin.Manual));
                SqlParameterBinder.Add(command, "$sortIndex", sortIndex);

                command.ExecuteNonQuery();
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
                // ON DELETE CASCADE removes the shortcuts too, so a deleted group
                // cannot leave rows behind that a later re-created group with the
                // same name would inherit.
                command.CommandText = "DELETE FROM Groups WHERE Id = $id;";
                SqlParameterBinder.Add(command, "$id", id.ToString());
                command.ExecuteNonQuery();
                return true;
            });
        }

        public List<ShortcutItem> GetShortcuts(Guid groupId)
        {
            var items = new List<ShortcutItem>();
            if (groupId == Guid.Empty) return items;

            using SqliteCommand command = _database.CreateCommand(
                SelectShortcut + " WHERE GroupId = $groupId ORDER BY SortOrder, CreatedAt;",
                ("$groupId", groupId.ToString()));

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                items.Add(ReadShortcut(reader));

            return items;
        }

        public void ReplaceShortcuts(Guid groupId, IEnumerable<ShortcutItem> items)
        {
            if (groupId == Guid.Empty) throw new ArgumentException("A group id is required.", nameof(groupId));

            var list = new List<ShortcutItem>();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item == null) continue;
                    item.GroupId = groupId;
                    if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
                    list.Add(item);
                }
            }

            _database.InTransaction(transaction =>
            {
                using (SqliteCommand delete = _database.Connection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM ShortcutItems WHERE GroupId = $groupId;";
                    SqlParameterBinder.Add(delete, "$groupId", groupId.ToString());
                    delete.ExecuteNonQuery();
                }

                foreach (ShortcutItem item in list)
                {
                    using SqliteCommand insert = _database.Connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = @"
INSERT INTO ShortcutItems (Id, GroupId, Name, Type, Target, Arguments, WorkingDirectory, IconSource,
                           IconCacheKey, RunAsAdministrator, WindowState, PreferredMonitor,
                           PreferredMonitorDeviceName, Enabled, SortOrder, CreatedAt, UpdatedAt,
                           IsLegacyWindowsApp, LegacyFilePath, IsAutoDetected)
VALUES ($id, $groupId, $name, $type, $target, $arguments, $wd, $icon, $iconKey, $admin, $state,
        $monitor, $monitorName, $enabled, $sort, $created, $updated, $legacyApp, $legacyPath, $auto);";

                    SqlParameterBinder.Add(insert, "$id", item.Id.ToString());
                    SqlParameterBinder.Add(insert, "$groupId", groupId.ToString());
                    SqlParameterBinder.Add(insert, "$name", SqlValue.Text(item.Name));
                    SqlParameterBinder.Add(insert, "$type", SqlValue.EnumText(item.Type));
                    SqlParameterBinder.Add(insert, "$target", SqlValue.Text(item.Target));
                    SqlParameterBinder.Add(insert, "$arguments", SqlValue.Text(item.Arguments));
                    SqlParameterBinder.Add(insert, "$wd", SqlValue.Text(item.WorkingDirectory));
                    SqlParameterBinder.Add(insert, "$icon", SqlValue.Text(item.IconSource));
                    SqlParameterBinder.Add(insert, "$iconKey", SqlValue.Text(item.IconCacheKey));
                    SqlParameterBinder.Add(insert, "$admin", SqlValue.Bool(item.RunAsAdministrator));
                    SqlParameterBinder.Add(insert, "$state", SqlValue.EnumText(item.WindowState));
                    SqlParameterBinder.Add(insert, "$monitor", SqlValue.EnumText(item.PreferredMonitor));
                    SqlParameterBinder.Add(insert, "$monitorName", SqlValue.Text(item.PreferredMonitorDeviceName));
                    SqlParameterBinder.Add(insert, "$enabled", SqlValue.Bool(item.Enabled));
                    SqlParameterBinder.Add(insert, "$sort", item.SortOrder);
                    SqlParameterBinder.Add(insert, "$created", SqlValue.Time(item.CreatedAt));
                    SqlParameterBinder.Add(insert, "$updated", SqlValue.Time(item.UpdatedAt));
                    SqlParameterBinder.Add(insert, "$legacyApp", SqlValue.Bool(item.IsLegacyWindowsApp));
                    SqlParameterBinder.Add(insert, "$legacyPath", SqlValue.Text(item.LegacyFilePath));
                    SqlParameterBinder.Add(insert, "$auto", SqlValue.Bool(item.IsAutoDetected));
                    insert.ExecuteNonQuery();
                }

                return true;
            });
        }

        private const string SelectGroup = @"
SELECT Id, Name, Description, IconId, Width, Theme, Opacity, CornerRadius, ShadowEnabled,
       BlurEnabled, AnimationEnabled, OpenAllEnabled, SortMode, MonitorPreference,
       PreferredMonitorDeviceName, CreatedAt, UpdatedAt, LegacyName FROM Groups";

        private const string SelectShortcut = @"
SELECT Id, GroupId, Name, Type, Target, Arguments, WorkingDirectory, IconSource, IconCacheKey,
       RunAsAdministrator, WindowState, PreferredMonitor, PreferredMonitorDeviceName, Enabled,
       SortOrder, CreatedAt, UpdatedAt, IsLegacyWindowsApp, LegacyFilePath,
       IsAutoDetected FROM ShortcutItems";

        internal static Group ReadGroup(IDataRecord record)
        {
            return new Group
            {
                Id = record.GetGuid("Id"),
                Name = record.GetString("Name"),
                Description = record.GetString("Description"),
                IconId = record.GetString("IconId"),
                Width = record.GetInt("Width", 5),
                Theme = record.GetEnum("Theme", GroupTheme.System),
                Opacity = record.GetDouble("Opacity", 10d),
                CornerRadius = record.GetInt("CornerRadius", 8),
                ShadowEnabled = record.GetBool("ShadowEnabled"),
                BlurEnabled = record.GetBool("BlurEnabled"),
                AnimationEnabled = record.GetBool("AnimationEnabled"),
                OpenAllEnabled = record.GetBool("OpenAllEnabled"),
                SortMode = record.GetEnum("SortMode", GroupSortMode.Manual),
                MonitorPreference = record.GetEnum("MonitorPreference", MonitorPreference.FollowCursor),
                PreferredMonitorDeviceName = record.GetString("PreferredMonitorDeviceName"),
                CreatedAt = record.GetTime("CreatedAt"),
                UpdatedAt = record.GetTime("UpdatedAt"),
                LegacyName = record.GetString("LegacyName")
            };
        }

        internal static ShortcutItem ReadShortcut(IDataRecord record)
        {
            return new ShortcutItem
            {
                Id = record.GetGuid("Id"),
                GroupId = record.GetGuid("GroupId"),
                Name = record.GetString("Name"),
                Type = record.GetEnum("Type", ShortcutType.Unknown),
                Target = record.GetString("Target"),
                Arguments = record.GetString("Arguments"),
                WorkingDirectory = record.GetString("WorkingDirectory"),
                IconSource = record.GetString("IconSource"),
                IconCacheKey = record.GetString("IconCacheKey"),
                RunAsAdministrator = record.GetBool("RunAsAdministrator"),
                WindowState = record.GetEnum("WindowState", WindowStatePreference.Normal),
                PreferredMonitor = record.GetEnum("PreferredMonitor", MonitorPreference.FollowCursor),
                PreferredMonitorDeviceName = record.GetString("PreferredMonitorDeviceName"),
                Enabled = record.GetBool("Enabled"),
                SortOrder = record.GetInt("SortOrder"),
                CreatedAt = record.GetTime("CreatedAt"),
                UpdatedAt = record.GetTime("UpdatedAt"),
                IsLegacyWindowsApp = record.GetBool("IsLegacyWindowsApp"),
                LegacyFilePath = record.GetString("LegacyFilePath"),
                IsAutoDetected = record.GetBool("IsAutoDetected")
            };
        }
    }
}
