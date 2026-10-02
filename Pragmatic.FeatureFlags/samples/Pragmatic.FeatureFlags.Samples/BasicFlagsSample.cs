using Pragmatic.FeatureFlags;
using Pragmatic.FeatureFlags.Providers;

namespace Pragmatic.FeatureFlags.Samples;

/// <summary>
/// Demonstrates the simplest usage: defining on/off flags and evaluating them.
/// </summary>
public static class BasicFlagsSample
{
    public static async Task RunAsync()
    {
        var store = new InMemoryFeatureFlagStore();

        store.Define(new FeatureFlagDefinition
        {
            Name = "new-checkout",
            Enabled = true,
            Description = "Enables the redesigned checkout flow.",
        });

        store.Define(new FeatureFlagDefinition
        {
            Name = "legacy-export",
            Enabled = false,
            Description = "Old CSV export, kept off while we migrate.",
        });

        var checkout = await store.IsEnabledAsync("new-checkout");
        var export = await store.IsEnabledAsync("legacy-export");
        var unknown = await store.IsEnabledAsync("does-not-exist");

        Console.WriteLine($"  new-checkout   -> {checkout}");
        Console.WriteLine($"  legacy-export  -> {export}");
        Console.WriteLine($"  does-not-exist -> {unknown}  (unknown flags evaluate to false)");
    }
}
