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
///     Scenario 7 — <see cref="IMigrationHook" />. A hook runs custom logic before and
///     after each individual schema change, inside the migration transaction. The
///     <see cref="MigrationStepContext" /> exposes the live connection + transaction, so a
///     hook can run additional SQL atomically with the change (e.g. backfill a new column).
///     A before-hook returning <c>false</c> skips the change.
/// </summary>
public static class MigrationHookSample
{
    public static async Task Run(string connectionString)
    {
        Console.WriteLine("--- Scenario 7: IMigrationHook — before/after each change, in-transaction ---");

        var hook = new AuditMigrationHook();
        var runner = BuildRunner(hook);

        // Apply V2 (Role column + Posts table/index/FK) to a fresh DB so the hook sees real changes.
        var context = new MigrationContext(connectionString, DesiredSchemas.V2, new MigrationOptions());
        var result = await runner.MigrateAsync(context);

        Console.WriteLine($"  migration success  : {result.Success}, changes applied: {result.ChangesApplied}");
        Console.WriteLine($"  hook before-events : {hook.BeforeCount}");
        Console.WriteLine($"  hook after-events  : {hook.AfterCount} (each had live transaction access: {hook.AllHadTransaction})");
        Console.WriteLine("  verdict            : hooks fire per-change and can run SQL on context.Connection/Transaction atomically.");
        Console.WriteLine();
    }

    private static MigrationRunner BuildRunner(IMigrationHook hook)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISchemaIntrospector>(new SqliteSchemaIntrospector());
        services.AddSingleton<ISqlMigrationGenerator>(new SqliteMigrationGenerator());
        services.AddSingleton<IConnectionFactory>(new SqliteRunnerConnectionFactory());
        var factory = new MigrationProviderFactory(services.BuildServiceProvider());

        return new MigrationRunner(
            factory,
            new SchemaDiffEngine(),
            hooks: [hook],
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
    ///     Logs each change and confirms it has live transaction access. A real hook would,
    ///     for example, populate a freshly-added NOT NULL column via
    ///     <c>context.Connection.CreateCommand()</c> with <c>cmd.Transaction = context.Transaction</c>.
    /// </summary>
    private sealed class AuditMigrationHook : IMigrationHook
    {
        public int BeforeCount { get; private set; }
        public int AfterCount { get; private set; }
        public bool AllHadTransaction { get; private set; } = true;

        // Null = apply to all databases.
        public string? DatabaseName => null;

        public Task<bool> BeforeChangeAsync(MigrationStepContext context, CancellationToken ct = default)
        {
            BeforeCount++;
            Console.WriteLine($"    BEFORE {context.Change.GetType().Name,-12} {context.Change.Description}");
            return Task.FromResult(true); // return false to skip this change
        }

        public Task AfterChangeAsync(MigrationStepContext context, CancellationToken ct = default)
        {
            AfterCount++;
            if (context.Transaction is null) AllHadTransaction = false;
            Console.WriteLine($"    AFTER  {context.Change.GetType().Name,-12} {context.Change.Description}");
            return Task.CompletedTask;
        }
    }
}
