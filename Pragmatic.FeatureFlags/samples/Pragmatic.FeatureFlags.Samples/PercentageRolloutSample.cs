using Pragmatic.FeatureFlags;
using Pragmatic.FeatureFlags.Providers;

namespace Pragmatic.FeatureFlags.Samples;

/// <summary>
/// Demonstrates a deterministic percentage rollout. The evaluator hashes
/// "{flagName}:{userId}" with SHA-256 into a bucket [0,100); the same user always
/// lands in the same bucket, so this sample is fully reproducible (no RNG).
/// The rollout percentage is supplied as Values[0] on a "percentage" rule.
/// </summary>
public static class PercentageRolloutSample
{
    public static async Task RunAsync()
    {
        var store = new InMemoryFeatureFlagStore();

        store.Define(new FeatureFlagDefinition
        {
            Name = "beta-dashboard",
            Enabled = false,
            Description = "Rolled out to 30% of users by deterministic bucketing.",
            Rules =
            [
                new FeatureFlagRule { Type = "percentage", Values = ["30"], Enabled = true },
            ],
        });

        string[] users = ["alice", "bob", "carol", "dave", "erin", "frank", "grace", "heidi", "ivan", "judy"];

        var enabledCount = 0;
        foreach (var user in users)
        {
            var context = new FeatureFlagContext { UserId = user };
            var enabled = await store.IsEnabledAsync("beta-dashboard", context);
            if (enabled)
            {
                enabledCount++;
            }

            Console.WriteLine($"  user={user,-6} -> {enabled}");
        }

        Console.WriteLine($"  {enabledCount}/{users.Length} users in the 30% bucket (stable across runs).");

        // Re-evaluating the same user yields the same result every time.
        var aliceFirst = await store.IsEnabledAsync("beta-dashboard", new FeatureFlagContext { UserId = "alice" });
        var aliceSecond = await store.IsEnabledAsync("beta-dashboard", new FeatureFlagContext { UserId = "alice" });
        Console.WriteLine($"  determinism check: alice={aliceFirst} == alice={aliceSecond} -> {aliceFirst == aliceSecond}");
    }
}
