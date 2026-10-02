namespace Pragmatic.Configuration;

/// <summary>
///     Represents a change to a configuration value, emitted by <see cref="IConfigurationStore.WatchAsync" />.
/// </summary>
/// <param name="Key">The configuration key that changed.</param>
/// <param name="OldValue">The previous value. A <c>null</c> value indicates the key did not exist before the change (newly created); it does not distinguish from an explicitly stored null.</param>
/// <param name="NewValue">The new value, or <c>null</c> if the key was removed.</param>
/// <param name="TenantId">The tenant the change applies to, or <c>null</c> for global configuration.</param>
/// <param name="Timestamp">When the change occurred.</param>
public sealed record ConfigurationChange(
    string Key,
    string? OldValue,
    string? NewValue,
    string? TenantId,
    DateTimeOffset Timestamp);
