namespace Pragmatic.FeatureFlags;

/// <summary>
///     Represents a change to a feature flag, emitted by <see cref="IFeatureFlagStore.WatchAsync"/>.
/// </summary>
/// <param name="FlagName">The name of the flag that changed.</param>
/// <param name="WasEnabled">The flag's enabled state before the change.</param>
/// <param name="IsEnabled">The flag's enabled state after the change.</param>
/// <param name="Timestamp">When the change occurred.</param>
public sealed record FeatureFlagChange(
    string FlagName,
    bool WasEnabled,
    bool IsEnabled,
    DateTimeOffset Timestamp);
