namespace Showcase.Booking.Infrastructure.FeatureFlags;

/// <summary>
/// Loyalty discount in V1 — gradually rolling out to 30% of users.
/// When enabled, V1 CreateReservation applies 10% loyalty discount, which V2 applies without the flag.
/// Demonstrates: percentage rollout with deterministic bucketing.
/// </summary>
public sealed class LoyaltyDiscountFlag : IFeatureFlag
{
    public static string Name => "loyalty-discount";
    public static string? Description => "V1 loyalty discount (10%) — gradual rollout";
}
