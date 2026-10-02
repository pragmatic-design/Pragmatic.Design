namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Default implementation: always the leader (single-instance or no Discovery).
/// </summary>
public sealed class AlwaysLeaderElection : IMigrationLeaderElection
{
    public Task<bool> TryBecomeLeaderAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task ReleaseLeadershipAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task WaitForLeaderCompletionAsync(CancellationToken ct = default) => Task.CompletedTask;
}
