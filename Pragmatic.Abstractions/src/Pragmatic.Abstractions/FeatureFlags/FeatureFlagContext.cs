namespace Pragmatic.FeatureFlags;

/// <summary>
///     Context for feature flag evaluation.
///     Provides targeting information such as tenant, user, plan, and custom properties.
/// </summary>
public sealed record FeatureFlagContext
{
    /// <summary>Tenant identifier for tenant-scoped flags.</summary>
    public string? TenantId { get; init; }

    /// <summary>User identifier for user-targeted flags.</summary>
    public string? UserId { get; init; }

    /// <summary>Subscription plan for tier-based flags (e.g., "free", "pro", "enterprise").</summary>
    public string? Plan { get; init; }

    /// <summary>Environment for environment-scoped flags.</summary>
    public string? Environment { get; init; }

    /// <summary>Custom properties for rule evaluation.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = EmptyProperties;

    private static readonly IReadOnlyDictionary<string, string> EmptyProperties
        = new Dictionary<string, string>();

    /// <summary>Creates an empty context (no targeting).</summary>
    public static FeatureFlagContext Empty { get; } = new();
}
