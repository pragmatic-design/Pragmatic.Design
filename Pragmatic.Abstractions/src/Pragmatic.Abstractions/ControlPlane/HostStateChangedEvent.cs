namespace Pragmatic.ControlPlane;

/// <summary>
///     A host transitioned between lifecycle states.
/// </summary>
/// <param name="SourceHostId">Identifier of the host that changed state.</param>
/// <param name="Timestamp">When the transition occurred.</param>
/// <param name="OldState">The state the host transitioned from.</param>
/// <param name="NewState">The state the host transitioned to.</param>
/// <param name="Reason">Optional human-readable reason for the transition.</param>
public sealed record HostStateChangedEvent(
    string SourceHostId,
    DateTimeOffset Timestamp,
    HostState OldState,
    HostState NewState,
    string? Reason) : ControlPlaneEvent(SourceHostId, Timestamp);
