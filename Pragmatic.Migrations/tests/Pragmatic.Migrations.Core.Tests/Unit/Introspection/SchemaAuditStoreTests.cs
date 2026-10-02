using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Core.Tests.Unit.Introspection;

/// <summary>
///     Tests <see cref="SchemaAuditStore"/> against a real in-memory SQLite database.
///     SQLite runs in-process with no external dependency, so this stays a deterministic unit test
///     while still exercising the real ADO.NET command/reader code paths (parameter binding,
///     idempotent DDL, and the "table-not-found ⇒ empty history" contract).
/// </summary>
public class SchemaAuditStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SqliteMigrationGenerator _generator = new();

    private static readonly SchemaVersion Schema =
        new(ImmutableArray<TableSchema>.Empty, "AppDb", "Sqlite");

    public async Task InitializeAsync() => await _connection.OpenAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await _connection.DisposeAsync().ConfigureAwait(false);

    [Fact]
    public async Task EnsureTable_OnFreshDatabase_CreatesAuditTable()
    {
        await SchemaAuditStore.EnsureTableAsync(_connection, _generator);

        // A second call must be idempotent (CREATE TABLE IF NOT EXISTS).
        var act = () => SchemaAuditStore.EnsureTableAsync(_connection, _generator);
        await act.Should().NotThrowAsync();

        // The table now exists, so reading history must succeed and be empty.
        var history = await SchemaAuditStore.ReadHistoryAsync(_connection);
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordMigration_ThenReadHistory_RoundTripsTheRecord()
    {
        await SchemaAuditStore.EnsureTableAsync(_connection, _generator);

        await SchemaAuditStore.RecordMigrationAsync(
            _connection, Schema, "CREATE TABLE Foo;", changeCount: 3, durationMs: 42,
            appliedBy: "tester");

        var history = await SchemaAuditStore.ReadHistoryAsync(_connection);

        history.Should().ContainSingle();
        var record = history[0];
        record.Hash.Should().Be(Schema.Hash);
        record.ChangeCount.Should().Be(3);
        record.DurationMs.Should().Be(42);
        record.AppliedBy.Should().Be("tester");
    }

    [Fact]
    public async Task RecordMigration_NullAppliedBy_DefaultsToMachineName()
    {
        await SchemaAuditStore.EnsureTableAsync(_connection, _generator);

        await SchemaAuditStore.RecordMigrationAsync(
            _connection, Schema, "-- sql", changeCount: 1, durationMs: 1, appliedBy: null);

        var history = await SchemaAuditStore.ReadHistoryAsync(_connection);

        history.Should().ContainSingle()
            .Which.AppliedBy.Should().Be(Environment.MachineName);
    }

    [Fact]
    public async Task ReadHistory_WhenTableDoesNotExist_ReturnsEmptyInsteadOfThrowing()
    {
        // No EnsureTable call — the audit table is absent. This must NOT throw; it represents
        // the very first run before any migration has been recorded.
        var history = await SchemaAuditStore.ReadHistoryAsync(_connection);

        history.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadHistory_OrdersMostRecentFirst()
    {
        await SchemaAuditStore.EnsureTableAsync(_connection, _generator);

        // Two genuinely different schemas, so their derived hashes differ too — the audit rows
        // must come back newest-first regardless of insertion order.
        var older = Schema;
        var newer = Schema with
        {
            Tables = [.. Schema.Tables, new TableSchema("Extra", null, [new ColumnSchema("Id", "TEXT", false, true)], [], [])]
        };

        // Insert with explicit, increasing AppliedAt so ordering is deterministic regardless of
        // clock resolution (the DDL default is datetime('now'), second-granularity).
        await InsertWithTimestampAsync(older, "2020-01-01 00:00:00");
        await InsertWithTimestampAsync(newer, "2030-01-01 00:00:00");

        var history = await SchemaAuditStore.ReadHistoryAsync(_connection);

        history.Should().HaveCount(2);
        history[0].Hash.Should().Be(newer.Hash);
        history[1].Hash.Should().Be(older.Hash);
    }

    private async Task InsertWithTimestampAsync(SchemaVersion schema, string appliedAt)
    {
        var cmd = _connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = """
                INSERT INTO "__PragmaticSchema"
                  ("Hash", "SchemaJson", "AppliedAt", "DurationMs", "ChangeCount", "SqlScript", "AppliedBy")
                VALUES (@hash, '{}', @appliedAt, 0, 0, '', 'tester')
                """;
            var p1 = cmd.CreateParameter();
            p1.ParameterName = "@hash";
            p1.Value = schema.Hash;
            cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter();
            p2.ParameterName = "@appliedAt";
            p2.Value = appliedAt;
            cmd.Parameters.Add(p2);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
