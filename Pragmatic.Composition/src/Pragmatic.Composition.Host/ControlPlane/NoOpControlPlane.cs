using System.Runtime.CompilerServices;
using Pragmatic.ControlPlane;

namespace Pragmatic.Composition.ControlPlane;

/// <summary>
///     Default control plane for monolith mode — returns only this host,
///     ignores commands, produces no events. Zero overhead.
/// </summary>
public sealed class NoOpControlPlane(IHostIdentity identity, IHostStatus status) : IControlPlane
{
    // Validate the injected dependencies once at construction so a DI misconfiguration surfaces as a
    // clear ArgumentNullException naming the parameter, not a confusing NRE on first GetAllHostsAsync.
    private readonly IHostIdentity _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private readonly IHostStatus _status = status ?? throw new ArgumentNullException(nameof(status));

    /// <inheritdoc />
    public bool IsConnected => false;

    /// <inheritdoc />
    public Task ReportStatusAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task<IReadOnlyList<HostInfo>> GetAllHostsAsync(CancellationToken ct = default)
    {
        var self = new HostInfo
        {
            HostId = _identity.HostId,
            HostName = _identity.HostName,
            HostType = _identity.HostType,
            State = _status.State,
            StateReason = _status.StateReason,
            MigrationStatus = _status.MigrationStatus,
            LastHeartbeat = DateTimeOffset.UtcNow,
            StartedAt = _identity.StartedAt,
        };

        return Task.FromResult<IReadOnlyList<HostInfo>>([self]);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ControlPlaneEvent> StreamEventsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // NoOp: never produces events, waits until cancelled
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        yield break;
    }

    /// <summary>
    ///     No-op send. Always returns <see cref="ControlPlaneError.NotConnected"/> because this
    ///     implementation has no transport.
    /// </summary>
    /// <remarks>
    ///     The <see cref="IControlPlane.SendCommandAsync"/> contract uses a nullable error as the
    ///     outcome: a <c>null</c> return means the command was accepted, a non-null
    ///     <see cref="ControlPlaneError"/> means it was rejected. Returning
    ///     <see cref="ControlPlaneError.NotConnected"/> here is therefore the correct, explicit
    ///     "no control plane attached" signal — callers MUST inspect the return, not assume success.
    ///     (Migrating the contract from nullable-error to a <c>Result</c> type is a cross-cutting
    ///     IControlPlane API change tracked for a control-plane API iteration.)
    /// </remarks>
    public Task<ControlPlaneError?> SendCommandAsync(
        string targetHostId, HostCommand command, CancellationToken ct = default)
        => Task.FromResult<ControlPlaneError?>(ControlPlaneError.NotConnected());

    /// <inheritdoc />
    public Task BroadcastEventAsync(ControlPlaneEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
}
