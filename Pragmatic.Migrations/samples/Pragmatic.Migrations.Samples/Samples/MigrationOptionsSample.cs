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
///     Scenario 9 — <see cref="MigrationOptions" /> driven by the fluent builder:
///     <list type="bullet">
///       <item><c>DryRun</c>  ← <c>builder.DryRun()</c>  — plan only, no DDL executed.</item>
///       <item><c>Force</c>   ← <c>builder.Force()</c>   — apply even breaking changes.</item>
///       <item><c>DatabaseFilter</c> ← <c>builder.OnlyDatabase&lt;T&gt;()</c> — restrict by logical DB name.</item>
///     </list>
///     Demonstrated against a fresh SQLite DB: DryRun plans V2, Force applies it, OnlyDatabase
///     skips a non-matching database.
/// </summary>
public static class MigrationOptionsSample
{
    public static async Task Run(string connectionString)
    {
        Console.WriteLine("--- Scenario 9: MigrationOptions — DryRun / Force / OnlyDatabase ---");

        var runner = BuildRunner();
        var desired = DesiredSchemas.V2;

        // 1) DryRun: compute the plan, execute nothing.
        var dryResult = await runner.MigrateAsync(
            new MigrationContext(connectionString, desired, new MigrationOptions { DryRun = true }));
        Console.WriteLine($"  DryRun  → success={dryResult.Success}, planned changes={dryResult.AppliedChanges.Length}, applied={dryResult.ChangesApplied} (nothing executed)");

        // 2) Force: apply for real. Force lets breaking changes through too (none here).
        var forced = await runner.MigrateAsync(
            new MigrationContext(connectionString, desired, new MigrationOptions { Force = true }));
        Console.WriteLine($"  Force   → success={forced.Success}, changes applied={forced.ChangesApplied}");

        // 3) OnlyDatabase<T>(): MigrationsBuilder.OnlyDatabase<TDb>() adds typeof(TDb).Name to
        //    DatabaseFilter. A schema whose DatabaseName is NOT in the filter is skipped.
        var filtered = new MigrationOptions { DatabaseFilter = ["Primary"] };
        var otherDb = desired with { DatabaseName = "Reporting" };
        var skipped = await runner.MigrateAsync(new MigrationContext(connectionString, otherDb, filtered));
        Console.WriteLine($"  OnlyDatabase<Primary>() → 'Reporting' skipped: success={skipped.Success}, changes={skipped.ChangesApplied} (not in filter)");

        Console.WriteLine("  verdict            : DryRun plans, Force applies (incl. breaking), OnlyDatabase scopes by logical DB.");
        Console.WriteLine();
    }

    private static MigrationRunner BuildRunner()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISchemaIntrospector>(new SqliteSchemaIntrospector());
        services.AddSingleton<ISqlMigrationGenerator>(new SqliteMigrationGenerator());
        services.AddSingleton<IConnectionFactory>(new SqliteRunnerConnectionFactory());
        var factory = new MigrationProviderFactory(services.BuildServiceProvider());

        return new MigrationRunner(factory, new SchemaDiffEngine(), leaderElection: new AlwaysLeaderElection());
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
}
