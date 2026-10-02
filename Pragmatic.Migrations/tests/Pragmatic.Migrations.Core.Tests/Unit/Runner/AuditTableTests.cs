#pragma warning disable CA2007 // ConfigureAwait in test code

using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;
using Pragmatic.Migrations.Core.Tests.Provider;

namespace Pragmatic.Migrations.Core.Tests.Unit.Runner;

/// <summary>
///     The audit table is written on every successful migration, so the name the DDL creates and
///     the name the INSERT targets must be the same one. If they diverge — a DDL that hard-codes
///     __PragmaticSchema while the INSERT honours <c>UseAuditTable</c> — any custom name fails
///     with "no such table" after the schema has already been committed.
/// </summary>
public class AuditTableTests
{
    private static SchemaVersion DesiredSchema() => new(
        [
            new TableSchema("Widgets", null,
                [new ColumnSchema("Id", "TEXT", false, true)],
                [], [])
        ],
        DatabaseName: "test",
        ProviderName: MigrationConstants.ProviderSqlite);

    private static async Task<MigrationResult> MigrateAsync(SqliteConnection conn, Action<MigrationsBuilder>? configure = null)
    {
        var services = new ServiceCollection();
        var builder = new MigrationsBuilder(services);
        builder.UseProvider(MigrationConstants.ProviderSqlite, _ => new NonOwningConnection(conn));
        configure?.Invoke(builder);
        builder.Build();

        await using var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<IMigrationRunner>().MigrateAsync(
            new MigrationContext(conn.ConnectionString, DesiredSchema(), provider.GetRequiredService<MigrationOptions>()));
    }

    private static async Task<long> CountRowsAsync(SqliteConnection conn, string table)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task MigrateAsync_DefaultAuditTable_RecordsTheRun()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var result = await MigrateAsync(conn);

        result.Success.Should().BeTrue(result.Error);
        (await CountRowsAsync(conn, MigrationConstants.AuditTableName)).Should().Be(1);
    }

    [Fact]
    public async Task MigrateAsync_CustomAuditTable_CreatesAndWritesThatTable()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var result = await MigrateAsync(conn, m => m.UseAuditTable("_MyHistory"));

        result.Success.Should().BeTrue(result.Error);
        (await CountRowsAsync(conn, "_MyHistory")).Should().Be(1);
    }

    [Fact]
    public async Task MigrateAsync_CustomAuditTable_DoesNotProposeDroppingTheDefaultOne()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        // A host that previously ran with the default name still has __PragmaticSchema on disk.
        // If introspection stops excluding it, the next diff proposes DROP TABLE __PragmaticSchema
        // — a breaking change that blocks startup for good.
        await SchemaAuditStore.EnsureTableAsync(conn, new SqliteMigrationGenerator());

        var result = await MigrateAsync(conn, m => m.UseAuditTable("_MyHistory"));

        result.Success.Should().BeTrue(result.Error);
        result.AppliedChanges.Should().NotContain(c => c.Description.Contains(MigrationConstants.AuditTableName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadHistoryAsync_CustomAuditTable_ReturnsTheRecordedRuns()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        await MigrateAsync(conn, m => m.UseAuditTable("_MyHistory"));

        var history = await SchemaAuditStore.ReadHistoryAsync(conn, auditTableName: "_MyHistory");

        history.Should().ContainSingle();
        history[0].ChangeCount.Should().Be(1);
    }
}
