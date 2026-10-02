using System.Collections.Immutable;
using System.Data.Common;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Introspection;

/// <summary>
///     Reads SQL Server schema from sys.* catalog views.
///     <para>
///     Four queries, whatever the size of the database: one for the tables, one for every column,
///     one for every index, one for every foreign key — then grouped in memory. The previous shape
///     issued three round-trips per table, so introspection cost grew with the schema.
///     </para>
/// </summary>
public sealed class SqlServerSchemaIntrospector : ISchemaIntrospector
{
    public string ProviderName => MigrationConstants.ProviderSqlServer;

    public IReadOnlyList<string> ExcludedTables { get; init; } = new[]
    {
        MigrationConstants.AuditTableName,
        MigrationConstants.EfMigrationsTableName,
        MigrationConstants.DataMigrationTableName,
        MigrationConstants.LockTableName
    };

    public async Task<SchemaVersion> IntrospectAsync(DbConnection connection, CancellationToken ct = default)
    {
        var tableNames = await ReadTableNamesAsync(connection, ct).ConfigureAwait(false);
        if (tableNames.Count == 0)
            return new SchemaVersion([]);

        // The bulk queries read every user table and are filtered against this set, which keeps the
        // exclusion logic in exactly one place.
        var wanted = new HashSet<(string Schema, string Table)>(tableNames);

        var columns = await ReadAllColumnsAsync(connection, wanted, ct).ConfigureAwait(false);
        var indexes = await ReadAllIndexesAsync(connection, wanted, ct).ConfigureAwait(false);
        var foreignKeys = await ReadAllForeignKeysAsync(connection, wanted, ct).ConfigureAwait(false);

        var tables = tableNames
            .Select(t => new TableSchema(
                t.Table,
                t.Schema,
                columns.TryGetValue(t, out var c) ? [.. c] : [],
                indexes.TryGetValue(t, out var i) ? [.. i] : [],
                foreignKeys.TryGetValue(t, out var f) ? [.. f] : []))
            .ToList();

        // The hash is derived by SchemaVersion itself, from the same canonical function the
        // compile-time schema uses — that is what makes the two comparable.
        return new SchemaVersion([.. tables]);
    }

    private async Task<List<(string Schema, string Table)>> ReadTableNamesAsync(
        DbConnection connection, CancellationToken ct)
    {
        var result = new List<(string, string)>();

        // Build parameterised exclusion list to avoid SQL injection on table names
        var excludedList = ExcludedTables.ToList();
        var paramNames = excludedList.Select((_, i) => $"@ex{i}").ToList();
        var inClause = paramNames.Count > 0 ? string.Join(", ", paramNames) : "'__no_match__'";

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"""
                SELECT s.name, t.name
                FROM sys.tables t
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE t.is_ms_shipped = 0
                  AND t.name NOT IN ({inClause})
                ORDER BY s.name, t.name
                """;

            for (var i = 0; i < excludedList.Count; i++)
                AddParam(cmd, paramNames[i], excludedList[i]);

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    result.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        return result;
    }

    private static async Task<Dictionary<(string, string), List<ColumnSchema>>> ReadAllColumnsAsync(
        DbConnection connection, HashSet<(string Schema, string Table)> wanted, CancellationToken ct)
    {
        var byTable = new Dictionary<(string, string), List<ColumnSchema>>();

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            // Primary-key membership comes from a single derived table instead of a correlated
            // subquery per column.
            cmd.CommandText = """
                SELECT s.name AS schema_name, t.name AS table_name,
                       c.name AS column_name, tp.name AS type_name, c.is_nullable AS is_nullable,
                       dc.definition AS default_definition,
                       c.max_length AS max_length, c.precision AS [precision], c.scale AS scale,
                       CASE WHEN pkc.column_id IS NOT NULL THEN 1 ELSE 0 END AS is_pk
                FROM sys.tables t
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                JOIN sys.columns c ON c.object_id = t.object_id
                JOIN sys.types tp ON c.user_type_id = tp.user_type_id
                LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
                LEFT JOIN (
                    SELECT ic.object_id, ic.column_id
                    FROM sys.index_columns ic
                    JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                    WHERE i.is_primary_key = 1
                ) pkc ON pkc.object_id = c.object_id AND pkc.column_id = c.column_id
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name, c.column_id
                """;

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                // Resolve column ordinals by name once — resilient to SELECT column-order changes.
                var oSchema = reader.GetOrdinal("schema_name");
                var oTable = reader.GetOrdinal("table_name");
                var oColumnName = reader.GetOrdinal("column_name");
                var oTypeName = reader.GetOrdinal("type_name");
                var oIsNullable = reader.GetOrdinal("is_nullable");
                var oDefault = reader.GetOrdinal("default_definition");
                var oMaxLen = reader.GetOrdinal("max_length");
                var oPrecision = reader.GetOrdinal("precision");
                var oScale = reader.GetOrdinal("scale");
                var oIsPk = reader.GetOrdinal("is_pk");

                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var key = (reader.GetString(oSchema), reader.GetString(oTable));
                    if (!wanted.Contains(key)) continue;

                    var typeName = reader.GetString(oTypeName);
                    var maxLen = reader.GetInt16(oMaxLen);
                    var precision = reader.GetByte(oPrecision);
                    var scale = reader.GetByte(oScale);

                    var sqlType = FormatSqlServerType(typeName, maxLen, precision, scale);
                    var nullable = reader.GetBoolean(oIsNullable);
                    var defaultVal = reader.IsDBNull(oDefault) ? null : reader.GetString(oDefault);
                    var isPk = reader.GetInt32(oIsPk) == 1;

                    // Clean up default expressions (remove parens: ((0)) → 0)
                    if (defaultVal is not null)
                        defaultVal = NormalizeDefaultExpression(defaultVal);

                    Add(byTable, key, new ColumnSchema(reader.GetString(oColumnName), sqlType, nullable, isPk, defaultVal));
                }
            }
        }

        return byTable;
    }

    /// <summary>
    ///     Strips fully-wrapping balanced parentheses from a SQL Server default definition.
    ///     SQL Server stores defaults double/triple-wrapped (e.g. <c>((0))</c>, <c>(((0)))</c>);
    ///     repeatedly removing one balanced wrapping pair normalises them to a single form so the
    ///     diff engine does not report spurious changes. A pair is only removed when the opening
    ///     paren matches the closing paren of the whole string (i.e. it wraps the entire expression).
    /// </summary>
    private static string NormalizeDefaultExpression(string definition)
    {
        var value = definition.Trim();
        while (value.Length >= 2 && value[0] == '(' && value[^1] == ')' && IsWrappingPair(value))
            value = value[1..^1].Trim();
        return value;
    }

    private static bool IsWrappingPair(string value)
    {
        // The first '(' wraps the whole expression only if its matching ')' is the last char.
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')')
            {
                depth--;
                if (depth == 0) return i == value.Length - 1;
            }
        }

        return false;
    }

    private static string FormatSqlServerType(string typeName, short maxLen, byte precision, byte scale) =>
        typeName switch
        {
            "nvarchar" when maxLen == -1 => "nvarchar(max)",
            "nvarchar" => $"nvarchar({maxLen / 2})",
            "varchar" when maxLen == -1 => "varchar(max)",
            "varchar" => $"varchar({maxLen})",
            "varbinary" when maxLen == -1 => "varbinary(max)",
            "varbinary" => $"varbinary({maxLen})",
            "decimal" or "numeric" => $"{typeName}({precision},{scale})",
            "datetime2" when scale == 7 => "datetime2",
            "datetime2" => $"datetime2({scale})",
            _ => typeName
        };

    private static async Task<Dictionary<(string, string), List<IndexSchema>>> ReadAllIndexesAsync(
        DbConnection connection, HashSet<(string Schema, string Table)> wanted, CancellationToken ct)
    {
        var byTable = new Dictionary<(string, string), List<IndexSchema>>();

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = """
                SELECT s.name AS schema_name, t.name AS table_name,
                       i.name, i.is_unique, i.has_filter, i.filter_definition,
                       STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS columns
                FROM sys.tables t
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                JOIN sys.indexes i ON i.object_id = t.object_id
                JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                WHERE t.is_ms_shipped = 0
                  AND i.is_primary_key = 0 AND i.type > 0
                GROUP BY s.name, t.name, i.name, i.is_unique, i.has_filter, i.filter_definition
                ORDER BY s.name, t.name, i.name
                """;

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var key = (reader.GetString(0), reader.GetString(1));
                    if (!wanted.Contains(key)) continue;

                    var name = reader.GetString(2);
                    var isUnique = reader.GetBoolean(3);
                    var filter = reader.GetBoolean(4) ? reader.GetString(5) : null;
                    var cols = reader.GetString(6).Split(',').ToImmutableArray();

                    Add(byTable, key, new IndexSchema(name, cols, isUnique, filter));
                }
            }
        }

        return byTable;
    }

    private static async Task<Dictionary<(string, string), List<ForeignKeySchema>>> ReadAllForeignKeysAsync(
        DbConnection connection, HashSet<(string Schema, string Table)> wanted, CancellationToken ct)
    {
        var byTable = new Dictionary<(string, string), List<ForeignKeySchema>>();

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = """
                SELECT s.name AS schema_name, t.name AS table_name,
                       fk.name, COL_NAME(fkc.parent_object_id, fkc.parent_column_id),
                       OBJECT_NAME(fkc.referenced_object_id), COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id),
                       fk.delete_referential_action
                FROM sys.foreign_keys fk
                JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                JOIN sys.tables t ON t.object_id = fk.parent_object_id
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name, fk.name
                """;

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var key = (reader.GetString(0), reader.GetString(1));
                    if (!wanted.Contains(key)) continue;

                    var deleteAction = reader.GetByte(6) switch
                    {
                        1 => ReferentialAction.Cascade,
                        2 => ReferentialAction.SetNull,
                        3 => ReferentialAction.Restrict,
                        _ => ReferentialAction.NoAction
                    };

                    Add(byTable, key, new ForeignKeySchema(
                        reader.GetString(2), reader.GetString(3),
                        reader.GetString(4), reader.GetString(5),
                        deleteAction));
                }
            }
        }

        return byTable;
    }

    private static void Add<T>(Dictionary<(string, string), List<T>> map, (string, string) key, T value)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        list.Add(value);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
