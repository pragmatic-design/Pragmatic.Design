using Pragmatic.FeatureFlags;
using Pragmatic.FeatureFlags.Providers;

namespace Pragmatic.FeatureFlags.Samples;

/// <summary>
/// Demonstrates the strongly-typed <see cref="IFeatureFlag"/> pattern. A flag is a
/// type that exposes its name via a <c>static abstract</c> member, so call sites use
/// the type instead of a magic string: <c>store.IsEnabledAsync&lt;NewCheckoutFlag&gt;()</c>.
/// </summary>
public static class StronglyTypedFlagsSample
{
    /// <summary>Strongly-typed handle for the "new-checkout" flag.</summary>
    public sealed class NewCheckoutFlag : IFeatureFlag
    {
        public static string Name => "new-checkout";
        public static string? Description => "Redesigned checkout flow.";
    }

    /// <summary>Strongly-typed handle for the "premium-reports" flag.</summary>
    public sealed class PremiumReportsFlag : IFeatureFlag
    {
        public static string Name => "premium-reports";
        public static string? Description => "Advanced reporting for paid tiers.";
    }

    public static async Task RunAsync()
    {
        var store = new InMemoryFeatureFlagStore();

        store.Define(new FeatureFlagDefinition { Name = NewCheckoutFlag.Name, Enabled = true });
        store.Define(new FeatureFlagDefinition { Name = PremiumReportsFlag.Name, Enabled = false });

        // FeatureFlagStoreExtensions.IsEnabledAsync<TFlag> reads TFlag.Name for you.
        var checkout = await store.IsEnabledAsync<NewCheckoutFlag>();
        var reports = await store.IsEnabledAsync<PremiumReportsFlag>();

        Console.WriteLine($"  IsEnabledAsync<NewCheckoutFlag>()    -> {checkout}");
        Console.WriteLine($"  IsEnabledAsync<PremiumReportsFlag>() -> {reports}");

        // GetDefinitionAsync<TFlag> is the typed equivalent of a name lookup.
        var definition = await store.GetDefinitionAsync<NewCheckoutFlag>();
        Console.WriteLine($"  GetDefinitionAsync<NewCheckoutFlag>() -> Name='{definition?.Name}', Enabled={definition?.Enabled}");
    }
}
