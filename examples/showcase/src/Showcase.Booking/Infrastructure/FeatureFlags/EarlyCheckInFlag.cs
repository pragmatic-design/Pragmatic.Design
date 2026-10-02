namespace Showcase.Booking.Infrastructure.FeatureFlags;

/// <summary>
/// Early check-in — enabled for specific premium tenants.
/// When enabled, allows check-in without time restrictions.
/// Demonstrates: tenant targeting rule.
/// </summary>
public sealed class EarlyCheckInFlag : IFeatureFlag
{
    public static string Name => "early-check-in";
    public static string? Description => "Allow early check-in for premium tenants";
}
