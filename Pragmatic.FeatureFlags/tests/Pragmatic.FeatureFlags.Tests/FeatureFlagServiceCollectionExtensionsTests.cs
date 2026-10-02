using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.FeatureFlags.Providers;

namespace Pragmatic.FeatureFlags.Tests;

public class FeatureFlagServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPragmaticFeatureFlags_RegistersInMemoryStore()
    {
        var services = new ServiceCollection();
        services.AddPragmaticFeatureFlags();

        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IFeatureFlagStore>();

        store.Should().NotBeNull();
        store.Should().BeOfType<InMemoryFeatureFlagStore>();
    }

    [Fact]
    public void AddPragmaticFeatureFlags_DoesNotReplaceExistingStore()
    {
        var customStore = new InMemoryFeatureFlagStore();
        var services = new ServiceCollection();
        services.AddSingleton<IFeatureFlagStore>(customStore);
        services.AddPragmaticFeatureFlags();

        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IFeatureFlagStore>();

        store.Should().BeSameAs(customStore);
    }

    [Fact]
    public void AddPragmaticFeatureFlags_Generic_RegistersCustomStore()
    {
        var services = new ServiceCollection();
        services.AddPragmaticFeatureFlags<InMemoryFeatureFlagStore>();

        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IFeatureFlagStore>();

        store.Should().NotBeNull();
        store.Should().BeOfType<InMemoryFeatureFlagStore>();
    }
}
