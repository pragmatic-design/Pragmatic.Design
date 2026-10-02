namespace Pragmatic.ControlPlane;

/// <summary>
///     Snapshot of a connected host as seen by the control plane.
/// </summary>
public sealed record HostInfo
{
    /// <summary>The host's static identity.</summary>
    public required string HostId { get; init; }

    /// <summary>Logical host name.</summary>
    public required string HostName { get; init; }

    /// <summary>Role in the topology.</summary>
    public required HostType HostType { get; init; }

    /// <summary>Current lifecycle state.</summary>
    public required HostState State { get; init; }

    /// <summary>Reason for current state.</summary>
    public string? StateReason { get; init; }

    /// <summary>Migration progress (null if not migrating).</summary>
    public MigrationStatus? MigrationStatus { get; init; }

    /// <summary>UTC timestamp of the last heartbeat received from this host.</summary>
    public required DateTimeOffset LastHeartbeat { get; init; }

    /// <summary>When the host started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    ///     Aggregated health status from all <see cref="IHostHealthContributor"/> instances.
    ///     Null when no contributors are registered or health data is not yet available.
    /// </summary>
    public ContributorHealthStatus? HealthStatus { get; init; }

    /// <summary>
    ///     Per-contributor health reports (keyed by contributor name).
    ///     Null when health data is not yet available.
    /// </summary>
    public IReadOnlyDictionary<string, ContributorHealthReport>? HealthContributors { get; init; }
}
