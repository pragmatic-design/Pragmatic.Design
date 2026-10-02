using System.Collections.Immutable;
using System.Data.Common;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Core.Tests.Unit.Runner;

/// <summary>
///     Verifies <see cref="IDataMigration" /> execution: each runs exactly once (tracked by
///     name in __PragmaticDataMigrations), ordered by <c>Order</c>, filtered by database.
///     Runs against a shared in-memory SQLite database kept alive across migration calls.
/// </summary>
public sealed class DataMigrationTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _keepAlive;

    public DataMigrationTests()
    {
        _connectionString = $"Data Source=DataMig-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task DataMigration_RunsOnce_AcrossMultipleMigrateCalls()
    {
        var migration = new CountingDataMigration("seed-widgets");
        var runner = CreateRunner(migration);

        var first = await runner.MigrateAsync(Context());
        var second = await runner.MigrateAsync(Context());

        first.Success.Should().BeTrue(first.Error);
        second.Success.Should().BeTrue(second.Error);
        migration.RunCount.Should().Be(1, "a data migration is tracked by name and runs once per database");
    }

    [Fact]
    public async Task DataMigrations_RunInOrderRegardlessOfRegistrationOrder()
    {
        var log = new List<string>();
        var runner = CreateRunner(
            new OrderedDataMigration("second", 20, log),
            new OrderedDataMigration("first", 10, log));

        await runner.MigrateAsync(Context());

        log.Should().Equal("first", "second");
    }

    [Fact]
    public async Task DataMigration_ForAnotherDatabase_IsSkipped()
    {
        var migration = new CountingDataMigration("other-db") { DatabaseName = "SomeOtherDatabase" };
        var runner = CreateRunner(migration);

        await runner.MigrateAsync(Context());

        migration.RunCount.Should().Be(0);
    }

    private MigrationContext Context() => new(
        _connectionString,
        new SchemaVersion(ImmutableArray.Create(
                new TableSchema("Widgets", null,
                    ImmutableArray.Create(new ColumnSchema("Id", "TEXT", false, true)),
                    [], [])),
            DatabaseName: "Test",
            ProviderName: "Sqlite"),
        new MigrationOptions());

    private static MigrationRunner CreateRunner(params IDataMigration[] migrations)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISchemaIntrospector>(new SqliteSchemaIntrospector());
        services.AddSingleton<ISqlMigrationGenerator>(new SqliteMigrationGenerator());
        services.AddSingleton<IConnectionFactory>(new SqliteConnectionFactory());
        var factory = new MigrationProviderFactory(services.BuildServiceProvider());

        return new MigrationRunner(
            factory,
            new SchemaDiffEngine(),
            dataMigrations: migrations,
            leaderElection: new AlwaysLeaderElection());
    }

    private sealed class SqliteConnectionFactory : IConnectionFactory
    {
        public string ProviderName => "Sqlite";

        public async Task<DbConnection> CreateOpenConnectionAsync(string connectionString, CancellationToken ct = default)
        {
            var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            return conn;
        }
    }

    private sealed class CountingDataMigration(string name) : IDataMigration
    {
        public string Name => name;
        public string? DatabaseName { get; init; }
        public int RunCount { get; private set; }

        public Task MigrateAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct = default)
        {
            RunCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class OrderedDataMigration(string name, int order, List<string> log) : IDataMigration
    {
        public string Name => name;
        public int Order => order;

        public Task MigrateAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct = default)
        {
            log.Add(name);
            return Task.CompletedTask;
        }
    }
}
