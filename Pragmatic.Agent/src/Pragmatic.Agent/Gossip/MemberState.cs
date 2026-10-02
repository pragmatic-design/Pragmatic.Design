namespace Pragmatic.Agent.Gossip;

/// <summary>Lifecycle state of a cluster member in the SWIM protocol.</summary>
internal enum MemberState
{
    /// <summary>Member is alive and responding to pings.</summary>
    Alive,

    /// <summary>Member failed to respond — under investigation via indirect probes.</summary>
    Suspect,

    /// <summary>Member confirmed dead — will be removed after propagation.</summary>
    Dead
}

/// <summary>A known member of the gossip cluster.</summary>
internal sealed class ClusterMember
{
    public required string Id { get; init; }

    // Host/Port are mutable: a restarted member can rejoin with the same Id but new coordinates.
    // Like the other mutable fields below, they are only written under MembershipTable's state lock.
    public required string Host { get; set; }
    public required int Port { get; set; }
    public MemberState State { get; set; } = MemberState.Alive;
    public long Incarnation { get; set; }
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SuspectSince { get; set; }

    public override string ToString() => $"{Id}@{Host}:{Port} [{State}]";
}
