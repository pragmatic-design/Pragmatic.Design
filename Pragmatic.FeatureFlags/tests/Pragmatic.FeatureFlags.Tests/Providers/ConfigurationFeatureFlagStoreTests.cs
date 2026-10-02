using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Configuration;
using Pragmatic.FeatureFlags.Configuration;

namespace Pragmatic.FeatureFlags.Tests.Providers;

/// <summary>
///     <see cref="ConfigurationFeatureFlagStore" /> reads from any <see cref="IConfiguration" />
///     source and honors reload tokens. Tests use an in-memory provider so reloads are
///     observable without touching disk.
/// </summary>
public class ConfigurationFeatureFlagStoreTests
{
    [Fact]
    public async Task SimpleBooleanShortForm_IsParsed()
    {
        var config = BuildConfig(new()
        {
            ["FeatureFlags:NewDashboard"] = "true",
            ["FeatureFlags:LegacyMode"] = "false"
        });

        using var store = new ConfigurationFeatureFlagStore(config);

        (await store.IsEnabledAsync("NewDashboard")).Should().BeTrue();
        (await store.IsEnabledAsync("LegacyMode")).Should().BeFalse();
    }

    [Fact]
    public async Task DefinitionWithRules_IsParsed_AndEvaluated()
    {
        var config = BuildConfig(new()
        {
            ["FeatureFlags:NewCheckout:Enabled"] = "false",
            ["FeatureFlags:NewCheckout:Description"] = "Roll out the new checkout flow",
            ["FeatureFlags:NewCheckout:Rules:0:Type"] = "tenant",
            ["FeatureFlags:NewCheckout:Rules:0:Values:0"] = "acme",
            ["FeatureFlags:NewCheckout:Rules:0:Enabled"] = "true"
        });

        using var store = new ConfigurationFeatureFlagStore(config);

        // No context — falls back to base Enabled (false).
        (await store.IsEnabledAsync("NewCheckout")).Should().BeFalse();
        // Tenant "acme" matches the rule and flips Enabled to true.
        (await store.IsEnabledAsync("NewCheckout", new FeatureFlagContext { TenantId = "acme" })).Should().BeTrue();
        // Other tenant — no rule match — base wins.
        (await store.IsEnabledAsync("NewCheckout", new FeatureFlagContext { TenantId = "other" })).Should().BeFalse();
    }

    [Fact]
    public async Task UnknownFlag_IsDisabled()
    {
        using var store = new ConfigurationFeatureFlagStore(BuildConfig([]));

        (await store.IsEnabledAsync("UnknownFlag")).Should().BeFalse();
    }

    [Fact]
    public async Task GetDefinition_ReturnsFullDefinition()
    {
        var config = BuildConfig(new()
        {
            ["FeatureFlags:AsyncReports:Enabled"] = "true",
            ["FeatureFlags:AsyncReports:Description"] = "Generate reports in the background"
        });

        using var store = new ConfigurationFeatureFlagStore(config);

        var def = await store.GetDefinitionAsync("AsyncReports");

        def.Should().NotBeNull();
        def!.Name.Should().Be("AsyncReports");
        def.Enabled.Should().BeTrue();
        def.Description.Should().Be("Generate reports in the background");
    }

    [Fact]
    public async Task GetAll_ListsFlags_SortedByName()
    {
        var config = BuildConfig(new()
        {
            ["FeatureFlags:Zebra"] = "true",
            ["FeatureFlags:Apple"] = "false"
        });

        using var store = new ConfigurationFeatureFlagStore(config);

        var all = await store.GetAllAsync();

        all.Select(f => f.Name).Should().Equal("Apple", "Zebra");
    }

    [Fact]
    public async Task CustomSectionName_IsHonored()
    {
        var config = BuildConfig(new()
        {
            ["Toggles:NewDashboard"] = "true"
        });

        using var store = new ConfigurationFeatureFlagStore(config, sectionName: "Toggles");

        (await store.IsEnabledAsync("NewDashboard")).Should().BeTrue();
    }

    [Fact]
    public async Task ConfigurationReload_EmitsFeatureFlagChange()
    {
        var initial = new Dictionary<string, string?>
        {
            ["FeatureFlags:NewDashboard"] = "false"
        };
        var source = new ReloadableMemoryConfigurationSource(initial);
        var config = new ConfigurationBuilder().Add(source).Build();

        using var store = new ConfigurationFeatureFlagStore(config);

        var collector = Task.Run(async () =>
        {
            await foreach (var change in store.WatchAsync())
                return change;
            return null;
        });

        // Wait a moment to ensure WatchAsync is enumerating before reload.
        await Task.Delay(50);

        source.Update(new Dictionary<string, string?>
        {
            ["FeatureFlags:NewDashboard"] = "true"
        });

        var observed = await collector.WaitAsync(TimeSpan.FromSeconds(2));

        observed.Should().NotBeNull();
        observed!.FlagName.Should().Be("NewDashboard");
        observed.WasEnabled.Should().BeFalse();
        observed.IsEnabled.Should().BeTrue();
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> data) =>
        new ConfigurationBuilder().AddInMemoryCollection(data).Build();

    /// <summary>Mutable in-memory configuration source that fires the reload token on Update.</summary>
    private sealed class ReloadableMemoryConfigurationSource(Dictionary<string, string?> initial) : IConfigurationSource
    {
        private readonly ReloadableMemoryConfigurationProvider _provider = new(initial);

        public IConfigurationProvider Build(IConfigurationBuilder builder) => _provider;

        public void Update(Dictionary<string, string?> data) => _provider.Replace(data);
    }

    private sealed class ReloadableMemoryConfigurationProvider(Dictionary<string, string?> initial) : ConfigurationProvider
    {
        public ReloadableMemoryConfigurationProvider() : this(new Dictionary<string, string?>()) { }

        static ReloadableMemoryConfigurationProvider() { }

        public override void Load()
        {
            Data = new Dictionary<string, string?>(initial, StringComparer.OrdinalIgnoreCase);
        }

        public void Replace(Dictionary<string, string?> data)
        {
            Data = new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase);
            OnReload();
        }
    }
}
