using Pragmatic.ControlPlane;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>A host status a test moves by hand.</summary>
public sealed class SettableHostStatus : IHostStatus
{
    public HostState State { get; private set; } = HostState.Starting;

    public string? StateReason { get; private set; }

    public DateTimeOffset StateChangedAt { get; private set; } = DateTimeOffset.UtcNow;

    public MigrationStatus? MigrationStatus { get; private set; }

    public void TransitionTo(HostState newState, string? reason = null)
    {
        State = newState;
        StateReason = reason;
        StateChangedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateMigrationProgress(MigrationStatus status) => MigrationStatus = status;
}
