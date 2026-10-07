using System;
using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TaskbarGroups.Data.Repositories
{
    /// <summary>
    /// Adds typed parameters to SQLite commands.
    /// </summary>
    /// <remarks>
    /// <c>SqliteParameterCollection.Add(string, object)</c> has no overload that
    /// infers the column type, and passing a C# string for an INTEGER column
    /// stores TEXT. Because SQLite is dynamically typed that does not throw - it
    /// stores, and the mismatch only shows up later as a wrong sort order or a
    /// failed comparison. Every parameter in this assembly therefore goes through
    /// here, so a column type and the type of the value written into it cannot
    /// drift apart.
    /// </remarks>
    internal static class SqlParameterBinder
    {
        internal static SqliteParameter Add(SqliteCommand command, string name, object? value)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            var parameter = command.CreateParameter();
            parameter.ParameterName = name;

            switch (value)
            {
                case null:
                    parameter.Value = DBNull.Value;
                    parameter.SqliteType = SqliteType.Text;
                    break;

                case string text:
                    parameter.Value = text;
                    parameter.SqliteType = SqliteType.Text;
                    break;

                case bool flag:
                    parameter.Value = flag ? 1 : 0;
                    parameter.SqliteType = SqliteType.Integer;
                    break;

                case int i:
                    parameter.Value = i;
                    parameter.SqliteType = SqliteType.Integer;
                    break;

                case long l:
                    parameter.Value = l;
                    parameter.SqliteType = SqliteType.Integer;
                    break;

                case short s:
                    parameter.Value = (int)s;
                    parameter.SqliteType = SqliteType.Integer;
                    break;

                case byte by:
                    parameter.Value = (int)by;
                    parameter.SqliteType = SqliteType.Integer;
                    break;

                case double d:
                    parameter.Value = d;
                    parameter.SqliteType = SqliteType.Real;
                    break;

                case float f:
                    parameter.Value = (double)f;
                    parameter.SqliteType = SqliteType.Real;
                    break;

                case decimal m:
                    parameter.Value = (double)m;
                    parameter.SqliteType = SqliteType.Real;
                    break;

                case Guid guid:
                    parameter.Value = guid.ToString();
                    parameter.SqliteType = SqliteType.Text;
                    break;

                case DateTimeOffset offset:
                    parameter.Value = offset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                    parameter.SqliteType = SqliteType.Text;
                    break;

                case DateTime moment:
                    parameter.Value = new DateTimeOffset(moment.ToUniversalTime(), TimeSpan.Zero)
                        .ToString("O", CultureInfo.InvariantCulture);
                    parameter.SqliteType = SqliteType.Text;
                    break;

                case Enum enumeration:
                    parameter.Value = enumeration.ToString();
                    parameter.SqliteType = SqliteType.Text;
                    break;

                default:
                    parameter.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    parameter.SqliteType = SqliteType.Text;
                    break;
            }

            command.Parameters.Add(parameter);
            return parameter;
        }

        /// <summary>
        /// Adds an optional time, writing SQL NULL rather than the string "null"
        /// when there is no value.
        /// </summary>
        internal static SqliteParameter AddOptionalTime(SqliteCommand command, string name, DateTimeOffset? value)
        {
            if (!value.HasValue) return Add(command, name, null);
            return Add(command, name, value.Value);
        }
    }
}