using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     The committed schema snapshot (<c>schema/workload.schema.json</c>, the file the generator reads its indexes from) against the
///     schema the workload really creates: every table, its columns in order with their primary-key mark, and every index with its
///     columns, read back from SQLite's catalog after the DDL ran. A snapshot that drifts from the DDL fails here, so PRAG3011 is
///     computed from the true schema.
/// </summary>
public class SchemaSnapshotTests
{
    private static readonly string Snapshot = TestPaths.Fixture("Pragmatic.Persistence.Workload", "schema", "workload.schema.json");

    [Fact]
    public async Task The_snapshot_is_the_schema_the_workload_creates()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var statement in Schema.Create(SqlDialect.Sqlite))
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }

        await ExtrasSchema.CreateAsync(connection, SqlDialect.Sqlite);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Snapshot));
        var tables = document.RootElement.GetProperty("tables").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!, StringComparer.Ordinal);

        // Every real table is in the snapshot and nothing else (views and the sequence's counter table are not tables of the model, nor an FTS5
        // index: the virtual table and its shadow tables, named after it, are the engine's).
        var realTables = await ReadAsync(connection, "SELECT name FROM sqlite_master m WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '\\_\\_%' ESCAPE '\\' "
            + "AND NOT EXISTS (SELECT 1 FROM sqlite_master v WHERE v.type = 'table' AND v.sql LIKE 'CREATE VIRTUAL TABLE%' AND (m.name = v.name OR m.name LIKE v.name || '\\_%' ESCAPE '\\')) ORDER BY name", r => r.GetString(0));
        Assert.Equal(realTables, tables.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var table in realTables)
        {
            // The columns, in order, with the primary key: PRAGMA table_xinfo (generated columns included) gives cid, name, type, notnull, dflt_value, pk, hidden.
            var realColumns = await ReadAsync(connection, $"SELECT name, pk > 0 FROM pragma_table_xinfo('{table}') ORDER BY cid", r => (Name: r.GetString(0), Pk: r.GetBoolean(1)));
            var snapshotColumns = tables[table].GetProperty("columns").EnumerateArray().Select(c => (Name: c.GetProperty("name").GetString()!, Pk: c.GetProperty("isPrimaryKey").GetBoolean())).ToList();
            Assert.True(realColumns.SequenceEqual(snapshotColumns), $"{table}: the snapshot's columns are [{string.Join(", ", snapshotColumns)}], the table's [{string.Join(", ", realColumns)}]");

            // The indexes the DDL created (the automatic ones of a PRIMARY KEY have no SQL), each with its columns in order.
            var realIndexes = new List<(string Name, string Columns)>();
            foreach (var index in await ReadAsync(connection, $"SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = '{table}' AND sql IS NOT NULL ORDER BY name", r => r.GetString(0)))
                realIndexes.Add((index, string.Join(",", await ReadAsync(connection, $"SELECT name FROM pragma_index_info('{index}') ORDER BY seqno", r => r.GetString(0)))));
            var snapshotIndexes = tables[table].GetProperty("indexes").EnumerateArray().Select(i => (Name: i.GetProperty("name").GetString()!, Columns: string.Join(",", i.GetProperty("columns").EnumerateArray().Select(c => c.GetString())))).OrderBy(i => i.Name, StringComparer.Ordinal).ToList();
            Assert.True(realIndexes.SequenceEqual(snapshotIndexes), $"{table}: the snapshot's indexes are [{string.Join("; ", snapshotIndexes)}], the table's [{string.Join("; ", realIndexes)}]");
        }
    }

    private static async Task<List<T>> ReadAsync<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> read)
    {
        var rows = new List<T>();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(read(reader));
        return rows;
    }
}
