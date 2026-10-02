namespace Pragmatic.FeatureFlags;

/// <summary>
///     Marker interface for strongly-typed feature flags.
///     Each flag is a type — enables generic methods like <c>IsEnabledAsync&lt;TFlag&gt;()</c>
///     with compile-time safety instead of magic strings.
/// </summary>
/// <example>
///     <code>
/// public sealed class LoyaltyDiscount : IFeatureFlag
/// {
///     public static string Name => "loyalty-discount";
///     public static string? Description => "10% loyalty discount — gradual rollout";
/// }
///
/// // Usage:
/// await store.IsEnabledAsync&lt;LoyaltyDiscount&gt;(context, ct);
/// </code>
/// </example>
public interface IFeatureFlag
{
    /// <summary>Unique name of the feature flag (used as store key).</summary>
    static abstract string Name { get; }

    /// <summary>Human-readable description (optional, used for seeding/diagnostics).</summary>
    static abstract string? Description { get; }
}
