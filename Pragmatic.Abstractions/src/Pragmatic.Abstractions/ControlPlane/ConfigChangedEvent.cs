namespace Pragmatic.ControlPlane;

/// <summary>
///     A configuration value was changed (pushed from admin/CLI).
/// </summary>
/// <param name="SourceHostId">Identifier of the host (or actor) that originated the change.</param>
/// <param name="Timestamp">When the change occurred.</param>
/// <param name="Key">The configuration key that changed.</param>
/// <param name="OldValue">The previous value, or <c>null</c> if the key did not exist before.</param>
/// <param name="NewValue">The new value, or <c>null</c> if the key was removed.</param>
/// <param name="TenantId">The tenant the change applies to, or <c>null</c> for global configuration.</param>
public sealed record ConfigChangedEvent(
    string SourceHostId,
    DateTimeOffset Timestamp,
    string Key,
    string? OldValue,
    string? NewValue,
    string? TenantId) : ControlPlaneEvent(SourceHostId, Timestamp);
