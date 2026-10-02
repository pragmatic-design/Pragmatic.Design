namespace Pragmatic.FeatureFlags;

/// <summary>
///     Definition of a feature flag, including its targeting rules.
/// </summary>
public sealed record FeatureFlagDefinition
{
    /// <summary>Unique name of the feature flag.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the flag is globally enabled (before rule evaluation).</summary>
    public bool Enabled { get; init; }

    /// <summary>Human-readable description.</summary>
    public string? Description { get; init; }

    /// <summary>Targeting rules evaluated in order. First match wins.</summary>
    public IReadOnlyList<FeatureFlagRule> Rules { get; init; } = [];
}
