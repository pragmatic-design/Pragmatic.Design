using Microsoft.Extensions.Logging;
using Pragmatic.ControlPlane;

namespace Pragmatic.Composition.ControlPlane;

/// <summary>
///     Handles <see cref="ExitMaintenanceCommand"/> — deactivates maintenance mode, and puts a draining or
///     drained instance back in the rotation.
/// </summary>
/// <remarks>
///     One way back for both ways out: maintenance (the host refuses with 503) and a drain (the host left the
///     rotation). A drained instance becomes <see cref="HostState.Ready"/> and reports it, which is
///     what puts it back in the rotation.
/// </remarks>
public sealed class ExitMaintenanceCommandHandler(
    MaintenanceHandleHolder handleHolder,
    IHostStatus hostStatus,
    IControlPlane controlPlane,
    ILogger<ExitMaintenanceCommandHandler> logger) : IHostCommandHandler<ExitMaintenanceCommand>
{
    /// <summary>
    ///     Deactivates maintenance mode by disposing the active handle taken from the
    ///     <see cref="MaintenanceHandleHolder" />, and returns a draining or drained host to
    ///     <see cref="HostState.Ready"/>. No-op when neither applies.
    /// </summary>
    /// <param name="command">The exit-maintenance command.</param>
    /// <param name="ct">A cancellation token for the status report.</param>
    public async Task HandleAsync(ExitMaintenanceCommand command, CancellationToken ct = default)
    {
        if (handleHolder.TakeHandle() is { } handle)
        {
            handle.Dispose();
            logger.LogInformation("Maintenance mode deactivated via control plane");
        }

        if (hostStatus.State is HostState.Draining or HostState.Drained)
        {
            hostStatus.TransitionTo(HostState.Ready);
            await controlPlane.ReportStatusAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Back in the rotation via control plane");
        }
    }
}
