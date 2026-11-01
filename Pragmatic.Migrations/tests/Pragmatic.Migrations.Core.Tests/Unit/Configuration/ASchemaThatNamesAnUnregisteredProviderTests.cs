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
///     A schema whose provider name matches no registration (a misspelt name, or the <c>"Generic"</c> the generator writes for an EF Core
///     provider it does not recognise) is treated as one that names none: the one provider registered takes it, and with several it is refused.
///     The name fell back to the non-keyed default, which every registration sets, so with several it resolved to the last one registered (#451).
/// </summary>
public class ASchemaThatNamesAnUnregisteredProviderTests
{
    [Theory]
    [InlineData("Postgres")]
    [InlineData("Generic")]
    public void WithSeveralProviders_AnUnregisteredName_IsRefusedNamingItAndThem(string name)
    {
        var services = new ServiceCollection();
        var builder = new PragmaticBuilderMock();
        builder.Services.Returns(services);
        builder.UsePragmaticMigrations();
        var factory = services.BuildServiceProvider().GetRequiredService<MigrationProviderFactory>();

        // The control: the three this suite's drivers bring are registered, by name.
        factory.GetConnectionFactory(MigrationConstants.ProviderPostgreSql).ProviderName.Should().Be(MigrationConstants.ProviderPostgreSql);
        factory.GetConnectionFactory(MigrationConstants.ProviderSqlServer).ProviderName.Should().Be(MigrationConstants.ProviderSqlServer);
        factory.GetConnectionFactory(MigrationConstants.ProviderSqlite).ProviderName.Should().Be(MigrationConstants.ProviderSqlite);

        foreach (var resolve in new Action[]
                 {
                     () => factory.GetConnectionFactory(name),
                     () => factory.GetGenerator(name),
                     () => factory.GetIntrospector(name),
                 })
        {
            var thrown = Assert.Throws<InvalidOperationException>(resolve);
            thrown.Message.Should().Contain($"'{name}'");
            thrown.Message.Should().Contain(MigrationConstants.ProviderPostgreSql);
            thrown.Message.Should().Contain(MigrationConstants.ProviderSqlServer);
            thrown.Message.Should().Contain(MigrationConstants.ProviderSqlite);
        }
    }

    [Theory]
    [InlineData("Postgres")]
    [InlineData("Generic")]
    public void WithOneProvider_AnUnregisteredName_ResolvesIt(string name)
    {
        // A host with one driver whose generated schema says "Generic" migrates through that driver today, and keeps doing so.
        var services = new ServiceCollection();
        services.AddSingleton<ISqlMigrationGenerator>(new SqliteMigrationGenerator());
        services.AddSingleton<ISchemaIntrospector>(new SqliteSchemaIntrospector());
        services.AddSingleton<IConnectionFactory>(new GenericConnectionFactory(MigrationConstants.ProviderSqlite, cs => new SqliteConnection(cs)));
        var factory = new MigrationProviderFactory(services.BuildServiceProvider());

        factory.GetConnectionFactory(name).ProviderName.Should().Be(MigrationConstants.ProviderSqlite);
        factory.GetGenerator(name).Should().BeOfType<SqliteMigrationGenerator>();
        factory.GetIntrospector(name).Should().BeOfType<SqliteSchemaIntrospector>();
    }
}
