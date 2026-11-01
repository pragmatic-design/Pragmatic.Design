using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Extensions;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Sql;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Migrations.Core.Tests.Unit.Configuration;

/// <summary>
///     A schema that names no provider migrates through the one provider registered, and is refused when several are: each registration also
///     sets the non-keyed default, so a null name resolved to whichever was registered last, in silence (#441). This suite references three
///     drivers, so the builder registers PostgreSQL, SQL Server and SQLite, as an application with one database per driver has them.
/// </summary>
public class ASchemaThatNamesNoProviderTests
{
    private static MigrationProviderFactory FactoryOfAHostWithEveryDriverItReferences()
    {
        var services = new ServiceCollection();
        var builder = new PragmaticBuilderMock();
        builder.Services.Returns(services);
        builder.UsePragmaticMigrations();

        return services.BuildServiceProvider().GetRequiredService<MigrationProviderFactory>();
    }

    [Fact]
    public void WithSeveralProviders_ANullName_IsRefusedNamingThem()
    {
        var factory = FactoryOfAHostWithEveryDriverItReferences();

        // The control: the three are registered, by name.
        factory.GetConnectionFactory(MigrationConstants.ProviderPostgreSql).ProviderName.Should().Be(MigrationConstants.ProviderPostgreSql);
        factory.GetConnectionFactory(MigrationConstants.ProviderSqlServer).ProviderName.Should().Be(MigrationConstants.ProviderSqlServer);
        factory.GetConnectionFactory(MigrationConstants.ProviderSqlite).ProviderName.Should().Be(MigrationConstants.ProviderSqlite);

        foreach (var resolve in new Action[]
                 {
                     () => factory.GetConnectionFactory(null),
                     () => factory.GetGenerator(null),
                     () => factory.GetIntrospector(null),
                 })
        {
            var thrown = Assert.Throws<InvalidOperationException>(resolve);
            thrown.Message.Should().Contain(MigrationConstants.ProviderPostgreSql);
            thrown.Message.Should().Contain(MigrationConstants.ProviderSqlServer);
            thrown.Message.Should().Contain(MigrationConstants.ProviderSqlite);
            thrown.Message.Should().Contain(nameof(Pragmatic.Migrations.Schema.SchemaVersion.ProviderName));
        }
    }

    [Fact]
    public void WithOneProvider_ANullName_ResolvesIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISqlMigrationGenerator>(new SqliteMigrationGenerator());
        services.AddSingleton<ISchemaIntrospector>(new SqliteSchemaIntrospector());
        services.AddSingleton<IConnectionFactory>(new GenericConnectionFactory(MigrationConstants.ProviderSqlite, cs => new SqliteConnection(cs)));
        var factory = new MigrationProviderFactory(services.BuildServiceProvider());

        factory.GetConnectionFactory(null).ProviderName.Should().Be(MigrationConstants.ProviderSqlite);
        factory.GetGenerator(null).Should().BeOfType<SqliteMigrationGenerator>();
        factory.GetIntrospector(null).Should().BeOfType<SqliteSchemaIntrospector>();
    }
}
