using System.Data.Common;
using System.Globalization;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Introspection;

/// <summary>
///     Reads the schema of the connection's current database (<c>DATABASE()</c>) from MySQL's information_schema: five queries,
///     whatever the size of the database, grouped in memory. A type is its <c>COLUMN_TYPE</c> (<c>varchar(255)</c>,
///     <c>tinyint(1)</c>, <c>datetime(6)</c>), the spelling the generator writes; the tables carry no schema name.
/// </summary>
public sealed class MySqlSchemaIntrospector : ISchemaIntrospector
{
    public string ProviderName => MigrationConstants.ProviderMySql;

    public IReadOnlyList<string> ExcludedTables { get; init; } = new[]
    {
        MigrationConstants.AuditTableName,
        MigrationConstants.EfMigrationsTableName,
        MigrationConstants.DataMigrationTableName,
        MigrationConstants.LockTableName
    };

    public async Task<SchemaVersion> IntrospectAsync(DbConnection connection, CancellationToken ct = default)
    {
        var excluded = new HashSet<string>(ExcludedTables, StringComparer.OrdinalIgnoreCase);
        var tableNames = (await ReadAsync(connection,
                "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME",
                r => r.GetString(0), ct).ConfigureAwait(false))
            .Where(t => !excluded.Contains(t)).ToList();
        if (tableNames.Count == 0)
            return new SchemaVersion([]);
        var wanted = new HashSet<string>(tableNames, StringComparer.Ordinal);

        var columns = Group(await ReadAsync(connection, """
            SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT, COLUMN_KEY, DATA_TYPE, EXTRA
            FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME, ORDINAL_POSITION
            """, r => (r.GetString(0), new ColumnSchema(
                r.GetString(1),
                r.GetString(2),
                r.GetString(3) == "YES",
                r.GetString(5) == "PRI",
                Default(r.IsDBNull(4) ? null : r.GetString(4), r.GetString(6), r.IsDBNull(7) ? "" : r.GetString(7)))), ct).ConfigureAwait(false), wanted);

        var foreignKeyRows = await ReadAsync(connection, """
            SELECT k.TABLE_NAME, k.CONSTRAINT_NAME, k.COLUMN_NAME, k.REFERENCED_TABLE_NAME, k.REFERENCED_COLUMN_NAME, c.DELETE_RULE
            FROM information_schema.KEY_COLUMN_USAGE k
            JOIN information_schema.REFERENTIAL_CONSTRAINTS c
              ON c.CONSTRAINT_SCHEMA = k.CONSTRAINT_SCHEMA AND c.TABLE_NAME = k.TABLE_NAME AND c.CONSTRAINT_NAME = k.CONSTRAINT_NAME
            WHERE k.TABLE_SCHEMA = DATABASE() AND k.REFERENCED_TABLE_NAME IS NOT NULL
            ORDER BY k.TABLE_NAME, k.CONSTRAINT_NAME, k.ORDINAL_POSITION
            """, r => (r.GetString(0), new ForeignKeySchema(r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5) switch
            {
                "CASCADE" => ReferentialAction.Cascade,
                "SET NULL" => ReferentialAction.SetNull,
                "RESTRICT" => ReferentialAction.Restrict,
                _ => ReferentialAction.NoAction
            })), ct).ConfigureAwait(false);
        var foreignKeys = Group(foreignKeyRows, wanted);

        // ⚠️ MySQL creates an index for a foreign key whose column has none, named after the constraint. It is the key's,
        // not the schema's: reported, the diff would propose dropping it, and MySQL refuses to drop an index a key needs.
        var keyIndexes = new HashSet<(string, string)>(foreignKeyRows.Select(f => (f.Item1, f.Item2.Name)));
        var indexRows = await ReadAsync(connection, """
            SELECT TABLE_NAME, INDEX_NAME, NON_UNIQUE, COLUMN_NAME
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE() AND INDEX_NAME <> 'PRIMARY'
            ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX
            """, r => (Table: r.GetString(0), Index: r.GetString(1), Unique: Convert.ToInt64(r.GetValue(2), CultureInfo.InvariantCulture) == 0, Column: r.IsDBNull(3) ? null : r.GetString(3)), ct).ConfigureAwait(false);
        var indexes = Group(indexRows
            .Where(i => !keyIndexes.Contains((i.Table, i.Index)))
            .GroupBy(i => (i.Table, i.Index))
            .Select(g => (g.Key.Table, new IndexSchema(g.Key.Index, [.. g.Where(i => i.Column is not null).Select(i => i.Column!)], g.First().Unique))), wanted);

        var checks = Group(await ReadAsync(connection, """
            SELECT t.TABLE_NAME, c.CONSTRAINT_NAME, c.CHECK_CLAUSE
            FROM information_schema.TABLE_CONSTRAINTS t
            JOIN information_schema.CHECK_CONSTRAINTS c ON c.CONSTRAINT_SCHEMA = t.CONSTRAINT_SCHEMA AND c.CONSTRAINT_NAME = t.CONSTRAINT_NAME
            WHERE t.TABLE_SCHEMA = DATABASE() AND t.CONSTRAINT_TYPE = 'CHECK'
            ORDER BY t.TABLE_NAME, c.CONSTRAINT_NAME
            """, r => (r.GetString(0), new CheckConstraintSchema(r.GetString(1), r.GetString(2))), ct).ConfigureAwait(false), wanted);

        return new SchemaVersion([.. tableNames.Select(t => new TableSchema(
            t, null,
            columns.TryGetValue(t, out var c) ? [.. c] : [],
            indexes.TryGetValue(t, out var i) ? [.. i] : [],
            foreignKeys.TryGetValue(t, out var f) ? [.. f] : [],
            checks.TryGetValue(t, out var k) ? [.. k] : []))]);
    }

    /// <summary>
    ///     A column's default as the schema writes it. MySQL reports a text column's literal default without its quotes
    ///     (<c>pending</c> for <c>'pending'</c>); an expression default is marked DEFAULT_GENERATED and kept as reported.
    /// </summary>
    private static string? Default(string? reported, string dataType, string extra)
    {
        if (reported is null || extra.Contains("DEFAULT_GENERATED", StringComparison.OrdinalIgnoreCase))
            return reported;
        return dataType is "char" or "varchar" or "tinytext" or "text" or "mediumtext" or "longtext" or "enum" or "set"
            ? "'" + reported.Replace("'", "''") + "'"
            : reported;
    }

    private static async Task<List<T>> ReadAsync<T>(DbConnection connection, string sql, Func<DbDataReader, T> row, CancellationToken ct)
    {
        var rows = new List<T>();
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    rows.Add(row(reader));
            }
        }

        return rows;
    }

    private static Dictionary<string, List<T>> Group<T>(IEnumerable<(string Table, T Item)> rows, HashSet<string> wanted)
    {
        var byTable = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        foreach (var (table, item) in rows)
        {
            if (!wanted.Contains(table)) continue;
            if (!byTable.TryGetValue(table, out var list))
                byTable[table] = list = [];
            list.Add(item);
        }

        return byTable;
    }
}
