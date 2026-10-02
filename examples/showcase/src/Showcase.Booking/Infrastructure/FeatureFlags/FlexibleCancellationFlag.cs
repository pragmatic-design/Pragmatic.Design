namespace Showcase.Booking.Infrastructure.FeatureFlags;

/// <summary>
/// Extended cancellation window — enabled for enterprise plan tenants.
/// When enabled, extends the cancellation window from default to 72 hours.
/// Demonstrates: plan-based targeting rule.
/// </summary>
public sealed class FlexibleCancellationFlag : IFeatureFlag
{
    public static string Name => "flexible-cancellation";
    public static string? Description => "Extended 72h cancellation window for enterprise plans";
}
