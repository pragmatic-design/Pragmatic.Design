using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.FeatureFlags;
using Pragmatic.FeatureFlags.Configuration;

namespace Pragmatic.FeatureFlags.Samples;

/// <summary>
/// Demonstrates the configuration-backed store (<see cref="ConfigurationFeatureFlagStore"/>)
/// and runtime reload. An equivalent appsettings.json section would be:
///
/// <code>
/// {
///   "FeatureFlags": {
///     "new-checkout": true,
///     "beta-dashboard": {
///       "Enabled": false,
///       "Rules": [ { "Type": "percentage", "Values": [ "50" ], "Enabled": true } ]
///     }
///   }
/// }
/// </code>
///
/// In a real app the JSON file provider raises a reload token when the file changes on
/// disk and the store rebuilds its cache automatically. Here we use a small reloadable
/// in-memory provider so the sample is self-contained and the reload is observable
/// deterministically — the stock <c>AddInMemoryCollection</c> provider does not re-read
/// on <c>Reload()</c>.
/// </summary>
public static class ConfigurationStoreSample
{
    public static async Task RunAsync()
    {
        var source = new ReloadableMemoryConfigurationSource(InitialFlags());
        var configuration = new ConfigurationBuilder().Add(source).Build();

        var services = new ServiceCollection();
        // The DI extension registers a ConfigurationFeatureFlagStore bound to the
        // "FeatureFlags" section as the IFeatureFlagStore.
        services.AddSingleton<IConfiguration>(configuration);
        services.AddConfigurationFeatureFlagStore();

        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IFeatureFlagStore>();

        var before = await store.IsEnabledAsync("new-checkout");
        Console.WriteLine($"  new-checkout (initial config)   -> {before}");

        var betaContext = new FeatureFlagContext { UserId = "alice" };
        var betaBefore = await store.IsEnabledAsync("beta-dashboard", betaContext);
        Console.WriteLine($"  beta-dashboard alice (50% rule) -> {betaBefore}");

        // --- Simulate an appsettings.json edit at runtime ---
        // Replace the backing data and fire the reload token; the store honors it and
        // rebuilds its cache without a process restart.
        Console.WriteLine("  ...editing configuration at runtime (new-checkout -> false)...");
        var updated = InitialFlags();
        updated["FeatureFlags:new-checkout"] = "false";
        source.Update(updated);

        var after = await store.IsEnabledAsync("new-checkout");
        Console.WriteLine($"  new-checkout (after reload)     -> {after}");
    }

    private static Dictionary<string, string?> InitialFlags() => new(StringComparer.OrdinalIgnoreCase)
    {
        // A flag declared as a plain boolean (no-rules case).
        ["FeatureFlags:new-checkout"] = "true",
        // A flag declared as a sub-section with rules.
        ["FeatureFlags:beta-dashboard:Enabled"] = "false",
        ["FeatureFlags:beta-dashboard:Rules:0:Type"] = "percentage",
        ["FeatureFlags:beta-dashboard:Rules:0:Values:0"] = "50",
        ["FeatureFlags:beta-dashboard:Rules:0:Enabled"] = "true",
    };

    /// <summary>Mutable in-memory configuration source that fires the reload token on Update.</summary>
    private sealed class ReloadableMemoryConfigurationSource(Dictionary<string, string?> initial) : IConfigurationSource
    {
        private readonly ReloadableMemoryConfigurationProvider _provider = new(initial);

        public IConfigurationProvider Build(IConfigurationBuilder builder) => _provider;

        public void Update(Dictionary<string, string?> data) => _provider.Replace(data);
    }

    private sealed class ReloadableMemoryConfigurationProvider(Dictionary<string, string?> initial) : ConfigurationProvider
    {
        public override void Load()
            => Data = new Dictionary<string, string?>(initial, StringComparer.OrdinalIgnoreCase);

        public void Replace(Dictionary<string, string?> data)
        {
            Data = new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase);
            OnReload();
        }
    }
}
