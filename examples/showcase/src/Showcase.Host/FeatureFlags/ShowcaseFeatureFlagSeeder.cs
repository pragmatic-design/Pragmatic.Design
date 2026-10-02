using Pragmatic.FeatureFlags.Providers;
using Showcase.Booking.Infrastructure.FeatureFlags;

namespace Showcase.Host.FeatureFlags;

/// <summary>
/// Seeds demo feature flags on startup.
/// In production, flags would come from a database or remote provider (LaunchDarkly, Azure App Configuration).
/// Demonstrates: programmatic flag definition with targeting rules using typed IFeatureFlag markers.
/// </summary>
public class ShowcaseFeatureFlagSeeder(
    IServiceProvider serviceProvider,
    ILogger<ShowcaseFeatureFlagSeeder> logger) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        var memoryStore = serviceProvider.GetService<InMemoryFeatureFlagStore>();
        if (memoryStore is null)
        {
            logger.LogDebug("InMemoryFeatureFlagStore not registered, skipping seeding");
            return Task.CompletedTask;
        }

        // The factories rather than `new FeatureFlagRule { Type = "…" }`: a misspelt Type is a rule that
        // never matches, and nothing says so.

        // Loyalty discount — percentage rollout to 30% of users
        Define<LoyaltyDiscountFlag>(memoryStore, FeatureFlagRule.Percentage(30));

        // Early check-in — tenant targeting for premium hotels
        Define<EarlyCheckInFlag>(memoryStore, FeatureFlagRule.Tenant("premium-hotel", "grand-resort"));

        // Flexible cancellation — plan-based (enterprise customers)
        Define<FlexibleCancellationFlag>(memoryStore, FeatureFlagRule.Plan("enterprise", "premium"));

        logger.LogInformation("Seeded {Count} demo feature flags", 3);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Defines a flag from its IFeatureFlag type — name and description derived from the type.
    /// </summary>
    private static void Define<TFlag>(InMemoryFeatureFlagStore store, params FeatureFlagRule[] rules)
        where TFlag : IFeatureFlag
    {
        store.Define(new FeatureFlagDefinition
        {
            Name = TFlag.Name,
            Enabled = false,
            Description = TFlag.Description,
            Rules = rules
        });
    }
}
