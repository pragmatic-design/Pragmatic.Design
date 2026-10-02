using System.Collections.Immutable;
using System.Data.Common;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Introspection;

/// <summary>
///     Reads PostgreSQL schema from information_schema and pg_catalog.
///     <para>
///     Four queries, whatever the size of the database: one for the tables, one for every column,
///     one for every index, one for every foreign key — then grouped in memory. The previous shape
///     issued three round-trips per table, so introspection cost grew with the schema and a
///     hundred-table database paid three hundred round-trips on every startup.
///     </para>
/// </summary>
public sealed class PostgreSqlSchemaIntrospector : ISchemaIntrospector
{
    // Catalogs that are never part of a user schema.
    private const string SystemSchemaFilter = "('pg_catalog', 'information_schema')";

    public string ProviderName => MigrationConstants.ProviderPostgreSql;

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

        // The bulk queries deliberately do not repeat the exclusion list — they read every user
        // schema and are filtered against the table set below, which is cheaper than parameterising
        // three more IN clauses and impossible to get out of sync with.
        var wanted = new HashSet<(string Schema, string Table)>(tableNames);

        var columns = await ReadAllColumnsAsync(connection, wanted, ct).ConfigureAwait(false);
        var indexes = await ReadAllIndexesAsync(connection, wanted, ct).ConfigureAwait(false);
        var foreignKeys = await ReadAllForeignKeysAsync(connection, wanted, ct).ConfigureAwait(false);
        var checks = await ReadAllCheckConstraintsAsync(connection, wanted, ct).ConfigureAwait(false);

        var tables = tableNames
            .Select(t => new TableSchema(
                t.Table,
                t.Schema,
                columns.TryGetValue(t, out var c) ? [.. c] : [],
                indexes.TryGetValue(t, out var i) ? [.. i] : [],
                foreignKeys.TryGetValue(t, out var f) ? [.. f] : [],
                checks.TryGetValue(t, out var k) ? [.. k] : []))
            .ToList();

        // The hash is derived by SchemaVersion itself, from the same canonical function the
        // compile-time schema uses — that is what makes the two comparable.
        return new SchemaVersion([.. tables]);
    }

    private async Task<List<(string Schema, string Table)>> ReadTableNamesAsync(
        DbConnection connection, CancellationToken ct)
    {
        var result = new List<(string, string)>();

        // Build parameterised exclusion list: @ex0, @ex1, ... to avoid SQL injection on table names
        var excludedList = ExcludedTables.ToList();
        var paramNames = excludedList.Select((_, i) => $"@ex{i}").ToList();
        var inClause = paramNames.Count > 0 ? string.Join(", ", paramNames) : "'__no_match__'";

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"""
                SELECT table_schema, table_name FROM information_schema.tables
                WHERE table_schema NOT IN {SystemSchemaFilter}
                  AND table_type = 'BASE TABLE'
                  AND table_name NOT IN ({inClause})
                ORDER BY table_schema, table_name
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
            // The primary-key columns are collected once in a CTE and LEFT JOINed, rather than with
            // a per-row EXISTS: the correlated form re-ran the constraint lookup for every column
            // in the database.
            cmd.CommandText = $"""
                WITH pk AS (
                    SELECT tc.table_schema, tc.table_name, kcu.column_name
                    FROM information_schema.table_constraints tc
                    JOIN information_schema.key_column_usage kcu
                        ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                    WHERE tc.constraint_type = 'PRIMARY KEY'
                )
                SELECT c.table_schema, c.table_name, c.column_name, c.is_nullable, c.column_default,
                       c.character_maximum_length, c.numeric_precision, c.numeric_scale, c.udt_name,
                       (pk.column_name IS NOT NULL) AS is_pk
                FROM information_schema.columns c
                LEFT JOIN pk
                    ON pk.table_schema = c.table_schema
                   AND pk.table_name = c.table_name
                   AND pk.column_name = c.column_name
                WHERE c.table_schema NOT IN {SystemSchemaFilter}
                ORDER BY c.table_schema, c.table_name, c.ordinal_position
                """;

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                // Resolve column ordinals by name once — resilient to SELECT column-order changes.
                var oSchema = reader.GetOrdinal("table_schema");
                var oTable = reader.GetOrdinal("table_name");
                var oColumnName = reader.GetOrdinal("column_name");
                var oIsNullable = reader.GetOrdinal("is_nullable");
                var oColumnDefault = reader.GetOrdinal("column_default");
                var oMaxLen = reader.GetOrdinal("character_maximum_length");
                var oPrecision = reader.GetOrdinal("numeric_precision");
                var oScale = reader.GetOrdinal("numeric_scale");
                var oUdtName = reader.GetOrdinal("udt_name");
                var oIsPk = reader.GetOrdinal("is_pk");

                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var key = (reader.GetString(oSchema), reader.GetString(oTable));
                    if (!wanted.Contains(key)) continue;

                    var dataType = reader.GetString(oUdtName); // udt_name is more precise for PG
                    var maxLen = reader.IsDBNull(oMaxLen) ? (int?)null : reader.GetInt32(oMaxLen);
                    var precision = reader.IsDBNull(oPrecision) ? (int?)null : reader.GetInt32(oPrecision);
                    var scale = reader.IsDBNull(oScale) ? (int?)null : reader.GetInt32(oScale);

                    var sqlType = FormatPgType(dataType, maxLen, precision, scale);
                    var nullable = reader.GetString(oIsNullable) == "YES";
                    var defaultVal = reader.IsDBNull(oColumnDefault) ? null : reader.GetString(oColumnDefault);
                    var isPk = reader.GetBoolean(oIsPk);

                    // Strip PostgreSQL-generated defaults like nextval(...)
                    if (defaultVal?.StartsWith("nextval(", StringComparison.Ordinal) == true)
                        defaultVal = null;

                    Add(byTable, key, new ColumnSchema(reader.GetString(oColumnName), sqlType, nullable, isPk, defaultVal));
                }
            }
        }

        return byTable;
    }

    private static string FormatPgType(string udtName, int? maxLen, int? precision, int? scale)
    {
        // Common PG udt_name mappings
        return udtName switch
        {
            "varchar" when maxLen.HasValue => $"varchar({maxLen.Value})",
            "varchar" => "text",
            "bpchar" when maxLen.HasValue => $"char({maxLen.Value})",
            "numeric" when precision.HasValue && scale.HasValue => $"numeric({precision.Value},{scale.Value})",
            "numeric" when precision.HasValue => $"numeric({precision.Value})",
            _ => udtName
        };
    }

    private static async Task<Dictionary<(string, string), List<IndexSchema>>> ReadAllIndexesAsync(
        DbConnection connection, HashSet<(string Schema, string Table)> wanted, CancellationToken ct)
    {
        var byTable = new Dictionary<(string, string), List<IndexSchema>>();

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"""
                SELECT n.nspname AS schema_name,
                       t.relname AS table_name,
                       i.relname AS index_name,
                       ix.indisunique,
                       pg_get_expr(ix.indpred, ix.indrelid) AS filter_expr,
                       array_agg(a.attname ORDER BY array_position(ix.indkey, a.attnum)) AS columns
                FROM pg_index ix
                JOIN pg_class t ON t.oid = ix.indrelid
                JOIN pg_class i ON i.oid = ix.indexrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
                WHERE NOT ix.indisprimary
                  AND n.nspname NOT IN {SystemSchemaFilter}
                GROUP BY n.nspname, t.relname, i.relname, ix.indisunique, ix.indpred, ix.indrelid
                ORDER BY n.nspname, t.relname, i.relname
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
                    var filter = reader.IsDBNull(4) ? null : reader.GetString(4);
                    var colsArray = reader.GetValue(5) as string[];
                    var cols = colsArray is not null ? [.. colsArray] : ImmutableArray<string>.Empty;

                    Add(byTable, key, new IndexSchema(name, cols, isUnique, filter));
                }
            }
        }

        return byTable;
    }

    /// <summary>
    ///     The check constraints the database holds, so the diff adds only what is missing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Without this the diff would compare a desired schema that declares checks against a
    ///         current one that never reports any, decide they are all missing, and reissue them on every
    ///         run — where the second run fails, because the constraint is already there.
    ///     </para>
    ///     <para>
    ///         NOT NULL is a check constraint in PostgreSQL's catalogue and is excluded: it belongs to the
    ///         column and is already diffed there, and reporting it here would make the engine try to drop
    ///         every column's nullability as an unknown constraint.
    ///     </para>
    /// </remarks>
    private static async Task<Dictionary<(string, string), List<CheckConstraintSchema>>> ReadAllCheckConstraintsAsync(
        DbConnection connection, HashSet<(string Schema, string Table)> wanted, CancellationToken ct)
    {
        var byTable = new Dictionary<(string, string), List<CheckConstraintSchema>>();

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"""
                SELECT n.nspname, c.relname, con.conname, pg_get_constraintdef(con.oid)
                FROM pg_constraint con
                JOIN pg_class c ON c.oid = con.conrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE con.contype = 'c'
                  AND NOT con.conname LIKE '%_not_null'
                  AND n.nspname NOT IN {SystemSchemaFilter}
                ORDER BY n.nspname, c.relname, con.conname
                """;

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var key = (reader.GetString(0), reader.GetString(1));
                    if (!wanted.Contains(key)) continue;

                    // The definition comes back as "CHECK ((expr))" — kept verbatim. Nothing compares
                    // expressions (see DiffCheckConstraints), so normalising it would buy nothing.
                    Add(byTable, key, new CheckConstraintSchema(reader.GetString(2), reader.GetString(3)));
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
            cmd.CommandText = $"""
                SELECT tc.table_schema, tc.table_name,
                       tc.constraint_name, kcu.column_name, ccu.table_name, ccu.column_name,
                       rc.delete_rule
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                    ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                JOIN information_schema.constraint_column_usage ccu
                    ON tc.constraint_name = ccu.constraint_name AND tc.table_schema = ccu.table_schema
                JOIN information_schema.referential_constraints rc
                    ON tc.constraint_name = rc.constraint_name AND tc.table_schema = rc.constraint_schema
                WHERE tc.constraint_type = 'FOREIGN KEY'
                  AND tc.table_schema NOT IN {SystemSchemaFilter}
                ORDER BY tc.table_schema, tc.table_name, tc.constraint_name
                """;

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var key = (reader.GetString(0), reader.GetString(1));
                    if (!wanted.Contains(key)) continue;

                    var deleteRule = reader.GetString(6) switch
                    {
                        "CASCADE" => ReferentialAction.Cascade,
                        "SET NULL" => ReferentialAction.SetNull,
                        "RESTRICT" => ReferentialAction.Restrict,
                        _ => ReferentialAction.NoAction
                    };

                    Add(byTable, key, new ForeignKeySchema(
                        reader.GetString(2), reader.GetString(3),
                        reader.GetString(4), reader.GetString(5),
                        deleteRule));
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
