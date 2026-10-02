namespace Pragmatic.FeatureFlags;

/// <summary>
///     Store for feature flags. Evaluation-aware — not a simple key-value store.
///     Unlike <see cref="Pragmatic.Configuration.IConfigurationStore"/>, feature flags
///     require context-based evaluation (targeting rules, percentage rollout, etc.)
/// </summary>
public interface IFeatureFlagStore
{
    /// <summary>Evaluates a flag without context (uses defaults).</summary>
    Task<bool> IsEnabledAsync(string flagName, CancellationToken ct = default);

    /// <summary>Evaluates a flag with targeting context (tenant, user, plan, etc.).</summary>
    Task<bool> IsEnabledAsync(string flagName, FeatureFlagContext context, CancellationToken ct = default);

    /// <summary>Gets the full definition of a flag, including rules.</summary>
    Task<FeatureFlagDefinition?> GetDefinitionAsync(string flagName, CancellationToken ct = default);

    /// <summary>Lists all defined flags.</summary>
    Task<IReadOnlyList<FeatureFlagDefinition>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Watches for flag changes.</summary>
    IAsyncEnumerable<FeatureFlagChange> WatchAsync(CancellationToken ct = default);
}
