using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Migrations.Tests.Provider;

/// <summary>
///     A run whose schema names no provider migrates through the one registered, and takes that provider's advisory lock. The lock was
///     taken with the provider name the context requested, <c>""</c>, which falls into the no-op lock: two instances could migrate the same
///     PostgreSQL database at once, while the generator, the introspector and the connection were the resolved provider's.
/// </summary>
public class ARunThatNamesNoProviderTakesTheLockTests(PostgreSqlContainerFixture fixture) : IClassFixture<PostgreSqlContainerFixture>
{
    [Fact]
    public async Task ARunWhoseSchemaNamesNoProvider_WaitsForTheLockAnotherSessionHolds()
    {
        var holder = new NpgsqlConnection(fixture.ConnectionString);
        await using var closeHolder = holder.ConfigureAwait(false);
        await holder.OpenAsync();
        var held = await MigrationLock.AcquireAsync(holder, MigrationConstants.ProviderPostgreSql);

        // One provider, registered as the default only, as a host referencing one driver has it. Not through MigrationsBuilder: this
        // suite references every driver, so the generator's registry gives the builder all five, and with several registered a null name
        // is refused (#441).
        var services = new ServiceCollection();
        services.AddSingleton<ISqlMigrationGenerator>(new PostgreSqlMigrationGenerator());
        services.AddSingleton<ISchemaIntrospector>(new PostgreSqlSchemaIntrospector());
        services.AddSingleton<IConnectionFactory>(new GenericConnectionFactory(MigrationConstants.ProviderPostgreSql, cs => new NpgsqlConnection(cs)));
        var provider = services.BuildServiceProvider();
        await using var disposeProvider = provider.ConfigureAwait(false);
        var runner = new MigrationRunner(new MigrationProviderFactory(provider), new SchemaDiffEngine());
        var context = new MigrationContext(fixture.ConnectionString, Schema(), new MigrationOptions());

        // PostgreSQL's advisory lock waits without a timeout: the run is cancelled while it waits.
        using (var waiting = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var thrown = await Record.ExceptionAsync(() => runner.MigrateAsync(context, waiting.Token));
            Assert.IsAssignableFrom<OperationCanceledException>(thrown);
        }

        (await TableExistsAsync(holder)).Should().BeFalse("the run waited for the lock instead of migrating");

        // The control: with the lock released the same run migrates, so it was the lock it waited for.
        await held.DisposeAsync();
        var result = await runner.MigrateAsync(context);
        result.Success.Should().BeTrue(result.Error ?? "");
        (await TableExistsAsync(holder)).Should().BeTrue();
    }

    /// <summary>One table, and no <see cref="SchemaVersion.ProviderName" />: the runner resolves the single registered provider.</summary>
    private static SchemaVersion Schema()
        => new(ImmutableArray.Create(new TableSchema("LockProbes", null,
            ImmutableArray.Create(new ColumnSchema("Id", "uuid", false, true)), [], [])));

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection)
    {
        var cmd = connection.CreateCommand();
        await using var dispose = cmd.ConfigureAwait(false);
        cmd.CommandText = "SELECT to_regclass('public.\"LockProbes\"') IS NOT NULL";
        return (bool)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;
    }
}
