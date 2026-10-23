using System.Data.Common;
using System.Globalization;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Introspection;

/// <summary>
///     Reads the connected user's schema from Oracle's USER_* dictionary views (23ai): six queries, whatever the size of the
///     schema, grouped in memory. A type is rebuilt in the spelling the generator writes: <c>VARCHAR2(450 CHAR)</c> when the
///     length is in characters, <c>NUMBER(10)</c>, <c>NUMBER(18,2)</c>, and <c>DATA_TYPE</c> as reported otherwise
///     (<c>TIMESTAMP(7)</c>, <c>BOOLEAN</c>, <c>CLOB</c>). The tables carry no schema name.
/// </summary>
/// <remarks>
///     The defaults are read from <c>DATA_DEFAULT_VC</c>, not the LONG <c>DATA_DEFAULT</c>; a default set to NULL (Oracle
///     cannot drop one) reads as none. The NOT NULL checks Oracle keeps as constraints are its own (system-named) and are not
///     reported, nor the indexes behind a primary or unique key.
/// </remarks>
public sealed class OracleSchemaIntrospector : ISchemaIntrospector
{
    public string ProviderName => MigrationConstants.ProviderOracle;

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
                "SELECT TABLE_NAME FROM USER_TABLES WHERE NESTED = 'NO' AND IOT_TYPE IS NULL AND TABLE_NAME NOT LIKE 'BIN$%' ORDER BY TABLE_NAME",
                r => r.GetString(0), ct).ConfigureAwait(false))
            .Where(t => !excluded.Contains(t)).ToList();
        if (tableNames.Count == 0)
            return new SchemaVersion([]);
        var wanted = new HashSet<string>(tableNames, StringComparer.Ordinal);

        var primaryKeys = new HashSet<(string, string)>(await ReadAsync(connection, """
            SELECT c.TABLE_NAME, k.COLUMN_NAME FROM USER_CONSTRAINTS c
            JOIN USER_CONS_COLUMNS k ON k.CONSTRAINT_NAME = c.CONSTRAINT_NAME
            WHERE c.CONSTRAINT_TYPE = 'P'
            """, r => (r.GetString(0), r.GetString(1)), ct).ConfigureAwait(false));

        var columns = Group(await ReadAsync(connection, """
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, DATA_PRECISION, DATA_SCALE, CHAR_LENGTH, CHAR_USED, NULLABLE, DATA_DEFAULT_VC
            FROM USER_TAB_COLUMNS ORDER BY TABLE_NAME, COLUMN_ID
            """, r => (r.GetString(0), new ColumnSchema(
                r.GetString(1),
                TypeOf(r.GetString(2), Number(r, 3), Number(r, 4), Number(r, 5), r.IsDBNull(6) ? null : r.GetString(6)),
                r.GetString(7) == "Y",
                primaryKeys.Contains((r.GetString(0), r.GetString(1))),
                DefaultOf(r.IsDBNull(8) ? null : r.GetString(8)))), ct).ConfigureAwait(false), wanted);

        var indexRows = await ReadAsync(connection, """
            SELECT i.TABLE_NAME, i.INDEX_NAME, i.UNIQUENESS, c.COLUMN_NAME
            FROM USER_INDEXES i
            JOIN USER_IND_COLUMNS c ON c.INDEX_NAME = i.INDEX_NAME
            WHERE i.INDEX_TYPE = 'NORMAL'
              AND i.INDEX_NAME NOT IN (SELECT INDEX_NAME FROM USER_CONSTRAINTS WHERE INDEX_NAME IS NOT NULL AND CONSTRAINT_TYPE IN ('P', 'U'))
            ORDER BY i.TABLE_NAME, i.INDEX_NAME, c.COLUMN_POSITION
            """, r => (Table: r.GetString(0), Index: r.GetString(1), Unique: r.GetString(2) == "UNIQUE", Column: r.GetString(3)), ct).ConfigureAwait(false);
        var indexes = Group(indexRows
            .GroupBy(i => (i.Table, i.Index))
            .Select(g => (g.Key.Table, new IndexSchema(g.Key.Index, [.. g.Select(i => i.Column)], g.First().Unique))), wanted);

        var foreignKeys = Group(await ReadAsync(connection, """
            SELECT c.TABLE_NAME, c.CONSTRAINT_NAME, k.COLUMN_NAME, r.TABLE_NAME, rk.COLUMN_NAME, c.DELETE_RULE
            FROM USER_CONSTRAINTS c
            JOIN USER_CONS_COLUMNS k ON k.CONSTRAINT_NAME = c.CONSTRAINT_NAME
            JOIN USER_CONSTRAINTS r ON r.CONSTRAINT_NAME = c.R_CONSTRAINT_NAME
            JOIN USER_CONS_COLUMNS rk ON rk.CONSTRAINT_NAME = r.CONSTRAINT_NAME AND rk.POSITION = k.POSITION
            WHERE c.CONSTRAINT_TYPE = 'R'
            ORDER BY c.TABLE_NAME, c.CONSTRAINT_NAME, k.POSITION
            """, r => (r.GetString(0), new ForeignKeySchema(r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5) switch
            {
                "CASCADE" => ReferentialAction.Cascade,
                "SET NULL" => ReferentialAction.SetNull,
                _ => ReferentialAction.NoAction
            })), ct).ConfigureAwait(false), wanted);

        var checks = Group(await ReadAsync(connection, """
            SELECT TABLE_NAME, CONSTRAINT_NAME, SEARCH_CONDITION_VC FROM USER_CONSTRAINTS
            WHERE CONSTRAINT_TYPE = 'C' AND GENERATED = 'USER NAME'
            ORDER BY TABLE_NAME, CONSTRAINT_NAME
            """, r => (r.GetString(0), new CheckConstraintSchema(r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2))), ct).ConfigureAwait(false), wanted);

        return new SchemaVersion([.. tableNames.Select(t => new TableSchema(
            t, null,
            columns.TryGetValue(t, out var c) ? [.. c] : [],
            indexes.TryGetValue(t, out var i) ? [.. i] : [],
            foreignKeys.TryGetValue(t, out var f) ? [.. f] : [],
            checks.TryGetValue(t, out var k) ? [.. k] : []))]);
    }

    /// <summary>The column's type in the generator's spelling.</summary>
    internal static string TypeOf(string dataType, int? precision, int? scale, int? charLength, string? charUsed) => dataType switch
    {
        "VARCHAR2" or "CHAR" => $"{dataType}({charLength}{(charUsed == "C" ? " CHAR" : "")})",
        "NVARCHAR2" or "NCHAR" => $"{dataType}({charLength})",
        "NUMBER" when precision is null => scale is 0 ? "NUMBER(*,0)" : "NUMBER",
        "NUMBER" => scale is null or 0 ? $"NUMBER({precision})" : $"NUMBER({precision},{scale})",
        "FLOAT" when precision is not null => $"FLOAT({precision})",
        _ => dataType
    };

    private static string? DefaultOf(string? reported)
    {
        var trimmed = reported?.Trim();
        return string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, "NULL", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    private static int? Number(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

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
