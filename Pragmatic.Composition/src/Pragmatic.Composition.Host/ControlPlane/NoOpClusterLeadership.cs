using Pragmatic.ControlPlane;

namespace Pragmatic.Composition.ControlPlane;

/// <summary>
///     Default <see cref="IClusterLeadership"/> for monolith mode: a single host is trivially the leader
///     of every scope. Zero overhead, no control-plane round-trips. Replaced by the Agent-backed lease
///     implementation when <c>UseAgent()</c> is called.
/// </summary>
public sealed class NoOpClusterLeadership : IClusterLeadership
{
    /// <inheritdoc />
    public Task<bool> TryAcquireAsync(string scope, CancellationToken ct = default) => Task.FromResult(true);

    /// <inheritdoc />
    public bool IsLeader(string scope) => true;

    /// <inheritdoc />
    public Task ReleaseAsync(string scope, CancellationToken ct = default) => Task.CompletedTask;
}
