using System.Collections.Immutable;
using System.Data.Common;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Core.Tests.Unit.Runner;

/// <summary>
///     Verifies <see cref="IMigrationHook" /> receives a usable <see cref="MigrationStepContext" /> —
///     in particular the active <see cref="DbTransaction" /> — so data migration is atomic with the
///     schema change. Run against real SQLite: without the transaction the hook's command throws
///     because a local transaction is pending.
/// </summary>
public class MigrationHookTests
{
    [Fact]
    public async Task MigrateAsync_AfterChangeHook_ReceivesUsableTransaction()
    {
        var hook = new RecordingHook();
        var runner = CreateSqliteRunner(hook);

        var desired = new SchemaVersion(ImmutableArray.Create(
                new TableSchema("Widgets", null,
                    ImmutableArray.Create(
                        new ColumnSchema("Id", "TEXT", false, true),
                        new ColumnSchema("Name", "TEXT", true, false)),
                    [], [])),
            DatabaseName: "Test",
            ProviderName: "Sqlite");

        var context = new MigrationContext("Data Source=:memory:", desired, new MigrationOptions());

        var result = await runner.MigrateAsync(context);

        result.Success.Should().BeTrue(result.Error);
        hook.AfterChangeCalled.Should().BeTrue();
        hook.TransactionWasNotNull.Should().BeTrue("the hook must receive the active migration transaction");
        hook.DataWriteSucceeded.Should().BeTrue("a command enrolled in the migration transaction must execute");
    }

    private static MigrationRunner CreateSqliteRunner(IMigrationHook hook)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISchemaIntrospector>(new SqliteSchemaIntrospector());
        services.AddSingleton<ISqlMigrationGenerator>(new SqliteMigrationGenerator());
        services.AddSingleton<IConnectionFactory>(new SqliteConnectionFactory());
        var factory = new MigrationProviderFactory(services.BuildServiceProvider());

        return new MigrationRunner(
            factory,
            new SchemaDiffEngine(),
            hooks: [hook],
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

    private sealed class RecordingHook : IMigrationHook
    {
        public string? DatabaseName => null;
        public bool AfterChangeCalled { get; private set; }
        public bool TransactionWasNotNull { get; private set; }
        public bool DataWriteSucceeded { get; private set; }

        public async Task AfterChangeAsync(MigrationStepContext context, CancellationToken ct = default)
        {
            AfterChangeCalled = true;
            TransactionWasNotNull = context.Transaction is not null;

            if (context.Change is not CreateTable { Table.Name: "Widgets" })
                return;

            // Data migration: write a row using the active migration transaction.
            // Without context.Transaction this throws on SQLite — a local transaction is pending.
            var cmd = context.Connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.Transaction = context.Transaction;
                cmd.CommandText = "INSERT INTO \"Widgets\" (\"Id\", \"Name\") VALUES ('w1', 'seed');";
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            DataWriteSucceeded = true;
        }
    }
}
