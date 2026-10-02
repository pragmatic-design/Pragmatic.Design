using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Database.Dialects;

namespace Pragmatic.Configuration.Database.Tests.Unit;

public sealed class DatabaseConfigurationExtensionsTests : IDisposable
{
    private readonly SqliteConnectionFactory _factory = new();

    [Fact]
    public void AddDatabaseConfigurationStore_RegistersConfigStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbConnectionFactory>(_factory);
        services.AddLogging();
        services.AddDatabaseConfigurationStore(opts => opts.Provider = DatabaseProvider.Sqlite);

        using var sp = services.BuildServiceProvider();
        var store = sp.GetService<IConfigurationStore>();
        store.Should().NotBeNull();
    }

    [Fact]
    public void AddDatabaseSecretStore_RegistersSecretStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbConnectionFactory>(_factory);
        services.AddLogging();
        services.AddDatabaseConfigurationStore(opts =>
        {
            opts.Provider = DatabaseProvider.Sqlite;
            opts.EncryptionKey = "QUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUE=";
        });
        services.AddDatabaseSecretStore();

        using var sp = services.BuildServiceProvider();
        var store = sp.GetService<ISecretStore>();
        store.Should().NotBeNull();
    }

    [Fact]
    public void AddDatabaseSecretStore_NoEncryptionKey_ThrowsOnResolve()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbConnectionFactory>(_factory);
        services.AddLogging();
        services.AddDatabaseConfigurationStore(opts => opts.Provider = DatabaseProvider.Sqlite);
        services.AddDatabaseSecretStore();

        using var sp = services.BuildServiceProvider();
        var act = () => sp.GetRequiredService<ISecretStore>();
        act.Should().Throw<InvalidOperationException>();
    }

    public void Dispose() => _factory.Dispose();
}
