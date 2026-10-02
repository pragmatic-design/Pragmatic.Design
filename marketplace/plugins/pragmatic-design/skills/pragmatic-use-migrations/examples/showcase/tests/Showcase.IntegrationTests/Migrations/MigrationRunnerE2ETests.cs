using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pragmatic.Migrations;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Migrations;

/// <summary>
///     End-to-end tests for <see cref="MigrationRunner" /> itself against a real PostgreSQL
///     database. The other migration tests exercise introspector, diff engine and SQL generator in
///     isolation and then run the SQL by hand — which left the runner (transactions, audit trail,
///     data migrations, honest failure reporting) with no coverage at all on a real database.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class MigrationRunnerE2ETests(PostgresFixture fixture)
{
    private static readonly string[] NoColumns = [];

    /// <summary>Creates an isolated database so a run never disturbs the shared fixture.</summary>
    private async Task<string> CreateDatabaseAsync(string name)
    {
        await using var admin = new NpgsqlConnection(fixture.AppConnectionString);
        await admin.OpenAsync();

        var drop = admin.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS {name} WITH (FORCE);";
        await drop.ExecuteNonQueryAsync();

        var create = admin.CreateCommand();
        create.CommandText = $"CREATE DATABASE {name};";
        await create.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(fixture.AppConnectionString) { Database = name }.ConnectionString;
    }

    private static (IMigrationRunner Runner, MigrationOptions Options, ServiceProvider Provider) BuildRunner(
        Action<MigrationsBuilder>? configure = null)
    {
        var services = new ServiceCollection();
        var builder = new MigrationsBuilder(services);
        builder.UseProvider(MigrationConstants.ProviderPostgreSql, cs => new NpgsqlConnection(cs));
        configure?.Invoke(builder);
        builder.Build();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMigrationRunner>(),
                provider.GetRequiredService<MigrationOptions>(),
                provider);
    }

    private static SchemaVersion Schema(params TableSchema[] tables) =>
        new([.. tables], DatabaseName: "e2e", ProviderName: MigrationConstants.ProviderPostgreSql);

    private static TableSchema Widgets(bool nameNullable = false, string nameType = "varchar(100)") =>
        new("Widgets", "public",
            [
                new ColumnSchema("Id", "uuid", false, true),
                new ColumnSchema("Name", nameType, nameNullable, false)
            ],
            [new IndexSchema("IX_Widgets_Name", ["Name"])],
            []);

    [Fact]
    public async Task Migrate_EmptyDatabase_CreatesSchemaAndRecordsTheRun()
    {
        var connectionString = await CreateDatabaseAsync("runner_e2e_create");
        var (runner, options, provider) = BuildRunner();
        await using var _ = provider;

        var result = await runner.MigrateAsync(new MigrationContext(connectionString, Schema(Widgets()), options));

        result.Success.Should().BeTrue(result.Error);
        result.ChangesApplied.Should().BeGreaterThan(0);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var schema = await new PostgreSqlSchemaIntrospector().IntrospectAsync(conn);
        schema.Tables.Should().Contain(t => t.Name == "Widgets");
        schema.Tables.First(t => t.Name == "Widgets").Indexes.Should().Contain(i => i.Name == "IX_Widgets_Name");

        // The audit trail is part of the contract, not a side effect.
        var history = await SchemaAuditStore.ReadHistoryAsync(conn);
        history.Should().ContainSingle();
        history[0].ChangeCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Migrate_RunTwice_SecondRunAppliesNothing()
    {
        var connectionString = await CreateDatabaseAsync("runner_e2e_idempotent");
        var (runner, options, provider) = BuildRunner();
        await using var _ = provider;

        var desired = Schema(Widgets());
        await runner.MigrateAsync(new MigrationContext(connectionString, desired, options));
        var second = await runner.MigrateAsync(new MigrationContext(connectionString, desired, options));

        second.Success.Should().BeTrue(second.Error);
        second.ChangesApplied.Should().Be(0, "an unchanged schema must produce an empty diff");
    }

    [Fact]
    public async Task Migrate_BreakingChangeWithoutForce_FailsAndLeavesTheDatabaseUntouched()
    {
        var connectionString = await CreateDatabaseAsync("runner_e2e_breaking");
        var (runner, options, provider) = BuildRunner();
        await using var _ = provider;

        await runner.MigrateAsync(new MigrationContext(connectionString, Schema(Widgets(nameNullable: true)), options));

        // NULL → NOT NULL is breaking and must be refused without Force.
        var result = await runner.MigrateAsync(
            new MigrationContext(connectionString, Schema(Widgets(nameNullable: false)), options));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Breaking changes");

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        var schema = await new PostgreSqlSchemaIntrospector().IntrospectAsync(conn);
        schema.Tables.First(t => t.Name == "Widgets").Columns.First(c => c.Name == "Name")
            .IsNullable.Should().BeTrue("a blocked migration must change nothing");
    }

    [Fact]
    public async Task Migrate_FailingChange_RollsBackEverythingAndReportsTheFailure()
    {
        var connectionString = await CreateDatabaseAsync("runner_e2e_rollback");
        var (runner, options, provider) = BuildRunner(m => m.Force());
        await using var _ = provider;

        await runner.MigrateAsync(new MigrationContext(connectionString, Schema(Widgets(nameNullable: true)), options));

        // Put a row in that cannot satisfy NOT NULL, so the migration is guaranteed to fail
        // partway: the added column must be rolled back with it.
        await using (var seed = new NpgsqlConnection(connectionString))
        {
            await seed.OpenAsync();
            var cmd = seed.CreateCommand();
            cmd.CommandText = """INSERT INTO "Widgets" ("Id", "Name") VALUES (gen_random_uuid(), NULL);""";
            await cmd.ExecuteNonQueryAsync();
        }

        var desired = Schema(new TableSchema("Widgets", "public",
            [
                new ColumnSchema("Id", "uuid", false, true),
                new ColumnSchema("Name", "varchar(100)", false, false),
                new ColumnSchema("Extra", "text", true, false)
            ],
            [new IndexSchema("IX_Widgets_Name", ["Name"])],
            []));

        var result = await runner.MigrateAsync(new MigrationContext(connectionString, desired, options));

        result.Success.Should().BeFalse("SET NOT NULL cannot succeed with a NULL row present");
        result.Error.Should().Contain("Migration failed at step");
        result.Suggestions.Should().Contain(s => s.Contains("NULL", StringComparison.Ordinal));

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        var schema = await new PostgreSqlSchemaIntrospector().IntrospectAsync(conn);
        schema.Tables.First(t => t.Name == "Widgets").Columns
            .Should().NotContain(c => c.Name == "Extra", "the whole migration is one transaction");
    }

    [Fact]
    public async Task Migrate_WithDataMigration_RunsItOnceAndTracksIt()
    {
        var connectionString = await CreateDatabaseAsync("runner_e2e_datamigration");
        var (runner, options, provider) = BuildRunner(m => m.AddDataMigration<SeedWidget>());
        await using var _ = provider;

        var desired = Schema(Widgets(nameNullable: true));

        var first = await runner.MigrateAsync(new MigrationContext(connectionString, desired, options));
        first.Success.Should().BeTrue(first.Error);

        // Second run: schema is unchanged and the data migration must NOT run again.
        var second = await runner.MigrateAsync(new MigrationContext(connectionString, desired, options));
        second.Success.Should().BeTrue(second.Error);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        var count = conn.CreateCommand();
        count.CommandText = """SELECT COUNT(*) FROM "Widgets" WHERE "Name" = 'seeded';""";
        Convert.ToInt64(await count.ExecuteScalarAsync()).Should().Be(1, "a data migration runs exactly once per database");
    }

    [Fact]
    public async Task Migrate_ManageDeclaredTablesOnly_LeavesForeignTablesAlone()
    {
        var connectionString = await CreateDatabaseAsync("runner_e2e_shared_db");

        // A table this host does not declare — as if another application shared the database.
        await using (var other = new NpgsqlConnection(connectionString))
        {
            await other.OpenAsync();
            var cmd = other.CreateCommand();
            cmd.CommandText = """CREATE TABLE "LegacyThings" ("Id" int PRIMARY KEY);""";
            await cmd.ExecuteNonQueryAsync();
        }

        var desired = Schema(Widgets());

        // Default behaviour: the unknown table is a DROP, i.e. a blocked breaking change.
        var (strictRunner, strictOptions, strictProvider) = BuildRunner();
        await using (strictProvider)
        {
            var blocked = await strictRunner.MigrateAsync(new MigrationContext(connectionString, desired, strictOptions));
            blocked.Success.Should().BeFalse("an undeclared table is proposed for DROP by default");
            blocked.AppliedChanges.Should().Contain(c => c.Description.Contains("LegacyThings", StringComparison.Ordinal));
        }

        // Opted in: foreign tables are simply not this host's business.
        var (tolerantRunner, tolerantOptions, tolerantProvider) = BuildRunner(m => m.ManageDeclaredTablesOnly());
        await using (tolerantProvider)
        {
            var result = await tolerantRunner.MigrateAsync(new MigrationContext(connectionString, desired, tolerantOptions));
            result.Success.Should().BeTrue(result.Error);
            result.AppliedChanges.Should().NotContain(c => c.Description.Contains("LegacyThings", StringComparison.Ordinal));
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        var schema = await new PostgreSqlSchemaIntrospector().IntrospectAsync(conn);
        schema.Tables.Should().Contain(t => t.Name == "LegacyThings");
        schema.Tables.Should().Contain(t => t.Name == "Widgets");
    }

    private sealed class SeedWidget : IDataMigration
    {
        public string Name => "e2e_seed_widget";

        public async Task MigrateAsync(
            System.Data.Common.DbConnection connection,
            System.Data.Common.DbTransaction transaction,
            CancellationToken ct = default)
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """INSERT INTO "Widgets" ("Id", "Name") VALUES (gen_random_uuid(), 'seeded');""";
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }
}
