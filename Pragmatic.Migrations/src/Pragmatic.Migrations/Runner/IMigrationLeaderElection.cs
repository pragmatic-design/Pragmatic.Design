namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Abstraction for distributed migration coordination.
///     Determines which instance in a cluster should execute migrations.
///     Default: <see cref="AlwaysLeaderElection"/> (single-instance, always runs).
///     Future: Discovery-based implementation using leader election protocol.
/// </summary>
public interface IMigrationLeaderElection
{
    /// <summary>
    ///     Attempts to become the migration leader. Returns true if this instance
    ///     should execute migrations, false if another instance is already the leader.
    ///     Non-leaders should wait until migrations are complete (via progress stream).
    /// </summary>
    Task<bool> TryBecomeLeaderAsync(CancellationToken ct = default);

    /// <summary>
    ///     Releases leadership after migrations are complete.
    ///     Other instances waiting in <see cref="WaitForLeaderCompletionAsync"/> will be unblocked.
    /// </summary>
    Task ReleaseLeadershipAsync(CancellationToken ct = default);

    /// <summary>
    ///     Waits until the leader instance has completed migrations.
    ///     Called by non-leader instances to block startup until the schema is ready.
    /// </summary>
    Task WaitForLeaderCompletionAsync(CancellationToken ct = default);
}
