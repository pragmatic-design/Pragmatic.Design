using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Database.Dialects;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Database.Tests.Unit;

/// <summary>
///     <c>ConnectionString</c> and a provider factory are enough to reach the database: the store connects
///     without a hand-written <see cref="IDbConnectionFactory" />.
/// </summary>
/// <remarks>
///     The option was documented as the way to point the store at its database and read by nothing: the
///     store asked for an <see cref="IDbConnectionFactory" />, no package provided one, and every consumer
///     wrote the same four lines.
/// </remarks>
public sealed class TheConnectionStringConnectsTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"pragmatic-config-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task AConnectionStringAndAProviderFactory_ReachTheDatabase()
    {
        using var provider = Build(o => o.ProviderFactory = SqliteFactory.Instance);
        var store = provider.GetRequiredService<IConfigurationStore>();

        await store.SetAsync("Mail:From", "ops@example.com");

        (await store.GetAsync("Mail:From")).Should().Be("ops@example.com");
    }

    /// <summary>A factory the application registers is what the store uses — the option does not replace it.</summary>
    [Fact]
    public async Task AFactoryTheApplicationRegisters_Wins()
    {
        using var own = new SqliteConnectionFactory();
        using var provider = Build(o => o.ProviderFactory = SqliteFactory.Instance, own);

        provider.GetRequiredService<IDbConnectionFactory>().Should().BeSameAs(own);
    }

    [Fact]
    public void NeitherAFactoryNorAProviderFactory_SaysWhatToSet()
    {
        using var provider = Build(_ => { });

        var failed = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IConfigurationStore>());

        failed.Message.Should().Contain("ProviderFactory").And.Contain("IDbConnectionFactory");
    }

    private ServiceProvider Build(Action<DatabaseConfigurationOptions> configure, IDbConnectionFactory? own = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (own is not null)
            services.AddSingleton(own);
        services.AddDatabaseConfigurationStore(o =>
        {
            o.Provider = DatabaseProvider.Sqlite;
            o.ConnectionString = $"Data Source={_file};Pooling=False";
            o.EnableChangePolling = false;
            configure(o);
        });
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (File.Exists(_file))
            File.Delete(_file);
    }
}
