namespace Pragmatic.ControlPlane;

/// <summary>
///     Elects a single host to own cluster-wide singleton work — deploy orchestration, a cluster-wide
///     maintenance decision, singleton background jobs. One leader per <c>scope</c> at a time.
/// </summary>
/// <remarks>
///     <para>
///         The default implementation is <c>NoOpClusterLeadership</c> (monolith mode — always leader).
///         <c>UseAgent()</c> replaces it with the Agent-backed lease implementation (KV compare-and-swap
///         plus a renewable lease over the gossip fabric).
///     </para>
///     <para>
///         This is a <b>soft</b> election: under a network/gossip partition each side may briefly believe
///         it holds the lease. That is acceptable for the advisory, re-drivable tasks it governs. It is
///         deliberately <b>not</b> used for database migrations, which require the hard mutual-exclusion
///         guarantee of <c>DatabaseLeaderElection</c> (a lock on the database itself).
///     </para>
/// </remarks>
public interface IClusterLeadership
{
    /// <summary>
    ///     Attempts to become the leader for <paramref name="scope"/>. Returns <c>true</c> if this host
    ///     now holds (or already held) the lease and will renew it; <c>false</c> if another host holds a
    ///     live lease or the control plane is unreachable. Idempotent — a leader calling it again renews.
    /// </summary>
    Task<bool> TryAcquireAsync(string scope, CancellationToken ct = default);

    /// <summary>
    ///     Whether this host currently believes it is the leader for <paramref name="scope"/> (its lease
    ///     was acquired and has not expired). A cheap local check — does not touch the control plane.
    /// </summary>
    bool IsLeader(string scope);

    /// <summary>
    ///     Voluntarily relinquishes leadership of <paramref name="scope"/> so another host can claim it
    ///     immediately instead of waiting for the lease to expire. No-op if this host is not the leader.
    /// </summary>
    Task ReleaseAsync(string scope, CancellationToken ct = default);
}
