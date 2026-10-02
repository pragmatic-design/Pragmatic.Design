using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.FeatureFlags;
using Pragmatic.FeatureFlags.Configuration;
using Pragmatic.FeatureFlags.Providers;
using Xunit;

namespace Pragmatic.FeatureFlags.Tests.Configuration;

public class ConfigurationFeatureFlagStoreExtensionsTests
{
    private static ServiceCollection ServicesWithConfig(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        return services;
    }

    [Fact]
    public void AddConfigurationFeatureFlagStore_RegistersConfigurationStore()
    {
        var services = ServicesWithConfig(new Dictionary<string, string?>
        {
            ["FeatureFlags:NewUI"] = "true",
        });

        services.AddConfigurationFeatureFlagStore();

        using var provider = services.BuildServiceProvider();
        var store = provider.GetService<IFeatureFlagStore>();
        store.Should().BeOfType<ConfigurationFeatureFlagStore>();
    }

    [Fact]
    public async Task AddConfigurationFeatureFlagStore_ResolvedStoreReadsRegisteredSection()
    {
        var services = ServicesWithConfig(new Dictionary<string, string?>
        {
            ["FeatureFlags:NewUI"] = "true",
        });

        services.AddConfigurationFeatureFlagStore();

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IFeatureFlagStore>();

        var result = await store.IsEnabledAsync("NewUI");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task AddConfigurationFeatureFlagStore_CustomSectionName_BindsThatSection()
    {
        var services = ServicesWithConfig(new Dictionary<string, string?>
        {
            ["Toggles:Beta:Enabled"] = "true",
            ["FeatureFlags:Beta:Enabled"] = "false",
        });

        services.AddConfigurationFeatureFlagStore("Toggles");

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IFeatureFlagStore>();

        var result = await store.IsEnabledAsync("Beta");
        result.Should().BeTrue();
    }

    [Fact]
    public void AddConfigurationFeatureFlagStore_IsIdempotent()
    {
        var services = ServicesWithConfig(new Dictionary<string, string?>
        {
            ["FeatureFlags:NewUI"] = "true",
        });

        services.AddConfigurationFeatureFlagStore();
        services.AddConfigurationFeatureFlagStore();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IFeatureFlagStore>().Should().ContainSingle();
    }

    [Fact]
    public void AddConfigurationFeatureFlagStore_NullServices_Throws()
    {
        var act = () => ((IServiceCollection)null!).AddConfigurationFeatureFlagStore();

        act.Should().Throw<ArgumentNullException>();
    }
}
