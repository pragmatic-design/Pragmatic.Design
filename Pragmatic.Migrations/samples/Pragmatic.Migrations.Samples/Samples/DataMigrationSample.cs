using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Samples.Samples;

/// <summary>
///     Scenario 4 — data migration. <see cref="IDataMigration" /> runs a named data
///     transformation exactly once per database, atomically with a tracking record in
///     <c>__PragmaticDataMigrations</c>. Re-running the migration is a no-op.
/// </summary>
public static class DataMigrationSample
{
    public static async Task Run(string connectionString)
    {
        Console.WriteLine("--- Scenario 4: IDataMigration — backfill once, tracked by name ---");

        var migration = new BackfillUsersRole();
        var runner = BuildRunner(migration);

        var desired = DesiredSchemas.V2; // schema already matches after Scenario 2 — fast path
        var context = new MigrationContext(connectionString, desired, new MigrationOptions());

        var first = await runner.MigrateAsync(context);
        var second = await runner.MigrateAsync(context);

        Console.WriteLine($"  run 1 success      : {first.Success}");
        Console.WriteLine($"  run 2 success      : {second.Success}");
        Console.WriteLine($"  backfill executed  : {BackfillUsersRole.RunCount} time(s) (expected: 1)");
        Console.WriteLine("  verdict            : the data migration is tracked by Name in __PragmaticDataMigrations — re-runs are no-ops.");
        Console.WriteLine();
    }

    private static MigrationRunner BuildRunner(IDataMigration migration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISchemaIntrospector>(new SqliteSchemaIntrospector());
        services.AddSingleton<ISqlMigrationGenerator>(new SqliteMigrationGenerator());
        services.AddSingleton<IConnectionFactory>(new SqliteRunnerConnectionFactory());
        var factory = new MigrationProviderFactory(services.BuildServiceProvider());

        return new MigrationRunner(
            factory,
            new SchemaDiffEngine(),
            dataMigrations: [migration],
            leaderElection: new AlwaysLeaderElection());
    }

    private sealed class SqliteRunnerConnectionFactory : IConnectionFactory
    {
        public string ProviderName => "Sqlite";

        public async Task<DbConnection> CreateOpenConnectionAsync(string connectionString, CancellationToken ct = default)
        {
            var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(ct);
            return conn;
        }
    }

    /// <summary>
    ///     A realistic body would issue UPDATEs against <c>connection</c> with
    ///     <c>cmd.Transaction = transaction</c> so the data change is atomic with the
    ///     tracking record. Kept as a no-op here to keep the sample self-contained.
    /// </summary>
    private sealed class BackfillUsersRole : IDataMigration
    {
        public static int RunCount { get; private set; }
        public string Name => "2026-05_BackfillUsersRole";

        public Task MigrateAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct = default)
        {
            RunCount++;
            return Task.CompletedTask;
        }
    }
}
