using System.Data.Common;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Introspection;

/// <summary>
///     Reads SQLite schema from sqlite_master and the pragma table-valued functions.
///     <para>
///     Five queries, whatever the size of the database. A bare <c>PRAGMA table_info(x)</c> can only
///     describe one table, which is what forced the old three-round-trips-per-table shape; the
///     <c>pragma_*</c> table-valued functions (SQLite 3.16+) can be joined against
///     <c>sqlite_master</c> instead, describing every table in one statement.
///     </para>
/// </summary>
public sealed class SqliteSchemaIntrospector : ISchemaIntrospector
{
    // Excludes SQLite's own internal tables from every bulk query.
    private const string UserTables = @"m.type = 'table' AND m.name NOT LIKE 'sqlite\_%' ESCAPE '\'";

    public string ProviderName => MigrationConstants.ProviderSqlite;

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

        var wanted = new HashSet<string>(tableNames, StringComparer.Ordinal);

        var columns = await ReadAllColumnsAsync(connection, wanted, ct).ConfigureAwait(false);
        var indexes = await ReadAllIndexesAsync(connection, wanted, ct).ConfigureAwait(false);
        var foreignKeys = await ReadAllForeignKeysAsync(connection, wanted, columns, ct).ConfigureAwait(false);

        var tables = tableNames
            .Select(name => new TableSchema(
                name,
                null,
                columns.TryGetValue(name, out var c) ? [.. c] : [],
                indexes.TryGetValue(name, out var i) ? [.. i] : [],
                foreignKeys.TryGetValue(name, out var f) ? [.. f] : []))
            .ToList();

        // The hash is derived by SchemaVersion itself, from the same canonical function the
        // compile-time schema uses — that is what makes the two comparable.
        return new SchemaVersion([.. tables]);
    }

    private async Task<List<string>> ReadTableNamesAsync(DbConnection connection, CancellationToken ct)
    {
        var result = new List<string>();

        // Build parameterised exclusion list to avoid SQL injection on table names
        var excludedList = ExcludedTables.ToList();
        var paramNames = excludedList.Select((_, i) => $"@ex{i}").ToList();
        var inClause = paramNames.Count > 0 ? string.Join(", ", paramNames) : "'__no_match__'";

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"""
                SELECT m.name FROM sqlite_master m
                WHERE {UserTables}
                  AND m.name NOT IN ({inClause})
                ORDER BY m.name
                """;

            for (var i = 0; i < excludedList.Count; i++)
                AddParam(cmd, paramNames[i], excludedList[i]);

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    result.Add(reader.GetString(0));
            }
        }

        return result;
    }

    private static async Task<Dictionary<string, List<ColumnSchema>>> ReadAllColumnsAsync(
        DbConnection connection, HashSet<string> wanted, CancellationToken ct)
    {
        var byTable = new Dictionary<string, List<ColumnSchema>>(StringComparer.Ordinal);

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"""
                SELECT m.name AS table_name, ti.name, ti.type, ti."notnull", ti.dflt_value, ti.pk
                FROM sqlite_master m
                JOIN pragma_table_info(m.name) ti
                WHERE {UserTables}
                ORDER BY m.name, ti.cid
                """;

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var table = reader.GetString(0);
                    if (!wanted.Contains(table)) continue;

                    var name = reader.GetString(1);
                    var type = reader.IsDBNull(2) ? "TEXT" : reader.GetString(2);
                    var notNull = reader.GetInt32(3) == 1;
                    var defaultVal = reader.IsDBNull(4) ? null : reader.GetString(4);
                    var isPk = reader.GetInt32(5) > 0;

                    Add(byTable, table, new ColumnSchema(name, type.ToUpperInvariant(), !notNull, isPk, defaultVal));
                }
            }
        }

        return byTable;
    }

    private static async Task<Dictionary<string, List<IndexSchema>>> ReadAllIndexesAsync(
        DbConnection connection, HashSet<string> wanted, CancellationToken ct)
    {
        // PRAGMA index_list exposes no filter, so a partial index read back without one looked
        // different from the desired schema on every run — the diff kept emitting DROP + ADD for an
        // index that was already correct. The WHERE clause is only recoverable from the stored
        // CREATE INDEX statement.
        var filters = await ReadIndexFiltersAsync(connection, ct).ConfigureAwait(false);

        // Ordered by (table, index, key position), so an index's columns arrive contiguously and in
        // key order — the grouping below relies on that.
        var byTable = new Dictionary<string, List<IndexSchema>>(StringComparer.Ordinal);
        var columnsOf = new Dictionary<(string Table, string Index), List<string>>();
        var uniqueOf = new Dictionary<(string Table, string Index), bool>();
        var order = new List<(string Table, string Index)>();

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"""
                SELECT m.name AS table_name, il.name AS index_name, il."unique", ii.name AS column_name
                FROM sqlite_master m
                JOIN pragma_index_list(m.name) il
                JOIN pragma_index_info(il.name) ii
                WHERE {UserTables}
                  AND il.name NOT LIKE 'sqlite\_autoindex\_%' ESCAPE '\'
                ORDER BY m.name, il.name, ii.seqno
                """;

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var table = reader.GetString(0);
                    if (!wanted.Contains(table)) continue;

                    var index = reader.GetString(1);
                    var key = (table, index);

                    if (!columnsOf.TryGetValue(key, out var cols))
                    {
                        columnsOf[key] = cols = [];
                        uniqueOf[key] = reader.GetInt32(2) == 1;
                        order.Add(key);
                    }

                    // An expression index has no column name at that position; it cannot be
                    // expressed as an IndexSchema, so skip the entry rather than fail the run.
                    if (!reader.IsDBNull(3))
                        cols.Add(reader.GetString(3));
                }
            }
        }

        foreach (var key in order)
        {
            Add(byTable, key.Table, new IndexSchema(
                key.Index,
                [.. columnsOf[key]],
                uniqueOf[key],
                filters.TryGetValue(key.Index, out var filter) ? filter : null));
        }

        return byTable;
    }

    /// <summary>
    ///     Recovers each index's partial-index predicate from the <c>CREATE INDEX</c> text SQLite
    ///     keeps in <c>sqlite_master</c>, since no PRAGMA reports it.
    /// </summary>
    private static async Task<Dictionary<string, string?>> ReadIndexFiltersAsync(
        DbConnection connection, CancellationToken ct)
    {
        var filters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            // Auto-created indexes (PK / UNIQUE constraints) have a NULL sql.
            cmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL";

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    filters[reader.GetString(0)] = ExtractWhereClause(reader.GetString(1));
            }
        }

        return filters;
    }

    /// <summary>
    ///     Extracts the predicate of a partial index from its CREATE INDEX statement, i.e. whatever
    ///     follows the WHERE that closes the column list. Returns null for a full index.
    /// </summary>
    internal static string? ExtractWhereClause(string createIndexSql)
    {
        // Scan for a top-level WHERE: one outside the parenthesised column list, so a column
        // expression containing the word is not mistaken for the predicate.
        var depth = 0;
        for (var i = 0; i < createIndexSql.Length; i++)
        {
            var c = createIndexSql[i];
            if (c == '(') { depth++; continue; }
            if (c == ')') { depth--; continue; }
            if (depth != 0) continue;

            if (char.ToUpperInvariant(c) != 'W' ||
                i + 5 > createIndexSql.Length ||
                !createIndexSql.AsSpan(i, 5).Equals("WHERE".AsSpan(), StringComparison.OrdinalIgnoreCase))
                continue;

            // Must be a standalone keyword, not the tail of an identifier.
            if (i > 0 && (char.IsLetterOrDigit(createIndexSql[i - 1]) || createIndexSql[i - 1] == '_'))
                continue;

            var predicate = createIndexSql.Substring(i + 5).Trim().TrimEnd(';').Trim();
            return predicate.Length > 0 ? predicate : null;
        }

        return null;
    }

    private static async Task<Dictionary<string, List<ForeignKeySchema>>> ReadAllForeignKeysAsync(
        DbConnection connection,
        HashSet<string> wanted,
        Dictionary<string, List<ColumnSchema>> columns,
        CancellationToken ct)
    {
        var byTable = new Dictionary<string, List<ForeignKeySchema>>(StringComparer.Ordinal);

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"""
                SELECT m.name AS table_name, fk."table", fk."from", fk."to", fk.on_delete
                FROM sqlite_master m
                JOIN pragma_foreign_key_list(m.name) fk
                WHERE {UserTables}
                ORDER BY m.name, fk.id, fk.seq
                """;

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var tableName = reader.GetString(0);
                    if (!wanted.Contains(tableName)) continue;

                    var refTable = reader.GetString(1);
                    var fromCol = reader.GetString(2);

                    // "to" is NULL when the FK targets the referenced table's primary key
                    // implicitly (REFERENCES Other, with no column list) — resolve it rather than
                    // throwing, since the referenced column is part of what the diff compares.
                    var toCol = reader.IsDBNull(3)
                        ? PrimaryKeyOf(columns, refTable) ?? fromCol
                        : reader.GetString(3);

                    var onDelete = (reader.IsDBNull(4) ? "" : reader.GetString(4)) switch
                    {
                        "CASCADE" => ReferentialAction.Cascade,
                        "SET NULL" => ReferentialAction.SetNull,
                        "RESTRICT" => ReferentialAction.Restrict,
                        _ => ReferentialAction.NoAction
                    };

                    var fkName = $"FK_{tableName}_{fromCol}_{refTable}";
                    Add(byTable, tableName, new ForeignKeySchema(fkName, fromCol, refTable, toCol, onDelete));
                }
            }
        }

        return byTable;
    }

    private static string? PrimaryKeyOf(Dictionary<string, List<ColumnSchema>> columns, string table) =>
        columns.TryGetValue(table, out var cols)
            ? cols.FirstOrDefault(c => c.IsPrimaryKey)?.Name
            : null;

    private static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
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
