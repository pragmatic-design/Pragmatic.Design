using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Cache;
using Pragmatic.Configuration.Extensions;
using Pragmatic.Configuration.Providers;
using Pragmatic.Configuration.Resolution;

namespace Pragmatic.Configuration.Tests.Unit;

public class ConfigurationServiceRegistrationTests
{
    [Fact]
    public void AddPragmaticConfiguration_RegistersDefaultServices()
    {
        var services = new ServiceCollection();

        services.AddPragmaticConfiguration();

        var provider = services.BuildServiceProvider();

        provider.GetService<IConfigurationStore>().Should().NotBeNull();
        // Read caching is ON by default, so the resolved store is the caching decorator.
        provider.GetService<IConfigurationStore>().Should().BeOfType<CachingConfigurationStore>();

        provider.GetService<ISecretStore>().Should().NotBeNull();
        // Read caching is ON by default, so the secret store is the caching decorator too (symmetric).
        provider.GetService<ISecretStore>().Should().BeOfType<CachingSecretStore>();

        // The default in-memory backend is writable, so IWritableSecretStore resolves (to the decorator,
        // which delegates writes to the inner store).
        provider.GetService<IWritableSecretStore>().Should().NotBeNull();

        provider.GetService<EnvironmentProfile>().Should().NotBeNull();
    }

    [Fact]
    public void AddPragmaticConfiguration_WithCachingDisabled_UsesBackingStoreDirectly()
    {
        var services = new ServiceCollection();

        services.AddPragmaticConfiguration(o => o.EnableReadCaching = false);

        var provider = services.BuildServiceProvider();

        // With caching opted out, no decorator wraps the backing in-memory store.
        provider.GetService<IConfigurationStore>().Should().BeOfType<InMemoryConfigurationStore>();
    }

    [Fact]
    public void AddPragmaticConfiguration_DefaultEnvironment_IsProduction()
    {
        var services = new ServiceCollection();
        services.AddPragmaticConfiguration();

        var provider = services.BuildServiceProvider();
        var env = provider.GetRequiredService<EnvironmentProfile>();

        // No IHostEnvironment registered → defaults to Production
        env.IsProduction.Should().BeTrue();
    }

    [Fact]
    public void AddPragmaticConfiguration_WithTag_SetsEnvironmentTag()
    {
        var services = new ServiceCollection();
        services.AddPragmaticConfiguration(o => o.EnvironmentTag = "eu-west");

        var provider = services.BuildServiceProvider();
        var env = provider.GetRequiredService<EnvironmentProfile>();

        env.Tag.Should().Be("eu-west");
    }

    [Fact]
    public void AddPragmaticConfiguration_ReturnsServiceCollectionForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddPragmaticConfiguration();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddPragmaticConfiguration_DoesNotReplaceExistingStore()
    {
        var services = new ServiceCollection();
        var customStore = new InMemoryConfigurationStore();
        services.AddSingleton<IConfigurationStore>(customStore);

        // Caching off so we assert the pure "don't discard the host's store" guarantee
        // without the caching decorator wrapping it.
        services.AddPragmaticConfiguration(o => o.EnableReadCaching = false);

        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IConfigurationStore>();
        store.Should().BeSameAs(customStore);
    }

    [Fact]
    public void AddPragmaticConfiguration_ConfigurationResolver_IsScoped()
    {
        var services = new ServiceCollection();
        services.AddPragmaticConfiguration();

        var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var resolver1 = scope1.ServiceProvider.GetRequiredService<IConfigurationResolver>();
        var resolver2 = scope2.ServiceProvider.GetRequiredService<IConfigurationResolver>();

        resolver1.Should().NotBeSameAs(resolver2);
    }
}
