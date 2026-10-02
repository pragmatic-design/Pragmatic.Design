using Pragmatic.FeatureFlags;
using Pragmatic.FeatureFlags.Providers;

namespace Pragmatic.FeatureFlags.Samples;

/// <summary>
/// Demonstrates the rule types (user, plan, property) and first-match-wins ordering.
/// Rules are evaluated top-to-bottom; the first rule that applies decides the result,
/// otherwise the flag falls back to its global Enabled value.
/// </summary>
public static class TargetingRulesSample
{
    public static async Task RunAsync()
    {
        var store = new InMemoryFeatureFlagStore();

        store.Define(new FeatureFlagDefinition
        {
            Name = "premium-reports",
            Enabled = false,
            Description = "Enabled for explicit users, the 'enterprise' plan, and EU regions.",
            Rules =
            [
                // user rule: exact UserId match (case-insensitive).
                new FeatureFlagRule { Type = "user", Values = ["admin-1"], Enabled = true },
                // plan rule: match the context Plan against allowed values.
                new FeatureFlagRule { Type = "plan", Values = ["enterprise"], Enabled = true },
                // property rule: Values[0] = key, Values[1..] = allowed values.
                new FeatureFlagRule { Type = "property", Values = ["region", "eu-west", "eu-central"], Enabled = true },
            ],
        });

        await Evaluate(store, "admin-1 (user rule)", new FeatureFlagContext { UserId = "admin-1" });

        await Evaluate(store, "enterprise plan (plan rule)", new FeatureFlagContext { Plan = "enterprise" });

        await Evaluate(store, "eu-west region (property rule)", new FeatureFlagContext
        {
            UserId = "user-7",
            Properties = new Dictionary<string, string> { ["region"] = "eu-west" },
        });

        await Evaluate(store, "free plan / us region (default)", new FeatureFlagContext
        {
            UserId = "user-42",
            Plan = "free",
            Properties = new Dictionary<string, string> { ["region"] = "us-east" },
        });

        await Evaluate(store, "empty context (default)", FeatureFlagContext.Empty);
    }

    private static async Task Evaluate(InMemoryFeatureFlagStore store, string label, FeatureFlagContext context)
    {
        var enabled = await store.IsEnabledAsync("premium-reports", context);
        Console.WriteLine($"  {label,-34} -> {enabled}");
    }
}
