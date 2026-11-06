using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Migrations.WithoutDriver.Tests;

/// <summary>
///     A host that references no SQL driver turns migrations on, and gets a runner (#555): the generated start applies the documents
///     engine's schemas only when one is registered, so refusing it left a host on the documents engine alone with no way to apply them. A
///     SQL database it is asked to migrate is still refused, by name, with the driver to reference.
/// </summary>
public class AHostWithoutASqlDriverTests
{
    [Fact]
    public void TurningMigrationsOn_WithoutADriver_RegistersTheRunner()
    {
        MigrationDriverRegistry.Providers.Should().BeEmpty("the control: this suite references no driver, so none registered itself");
        var services = new ServiceCollection();

        new MigrationsBuilder(services).Build();

        using var provider = services.BuildServiceProvider();
        provider.GetService<IMigrationRunner>().Should().NotBeNull(
            "the start applies the documents schemas through the migrations, and finds them on, by the runner");
    }

    [Fact]
    public async Task ASqlDatabase_IsStillRefused_WhenItIsMigrated_NamingTheDriver()
    {
        var services = new ServiceCollection();
        new MigrationsBuilder(services).Build();
        using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<IMigrationRunner>();

        var migrate = () => runner.MigrateAsync(new MigrationContext(
            "Host=nowhere", new SchemaVersion(ImmutableArray<TableSchema>.Empty, "OrdersDatabase", MigrationConstants.ProviderPostgreSql),
            provider.GetRequiredService<MigrationOptions>()));

        var refused = await migrate.Should().ThrowAsync<InvalidOperationException>();
        refused.Which.Message.Should().Contain("PostgreSql", "the message names the provider the database's schema uses");
        refused.Which.Message.Should().Contain("Npgsql", "and the driver package that would serve it");
    }
}
