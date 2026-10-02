using Microsoft.Extensions.Logging;
using Pragmatic.ControlPlane;

namespace Pragmatic.Composition.ControlPlane;

/// <summary>
///     Handles <see cref="DrainCommand"/> — takes the instance out of the rotation, lets what it has in flight
///     complete, and leaves it running, <see cref="HostState.Drained"/>.
/// </summary>
/// <remarks>
///     <para>
///         A drain is leaving the rotation, not stopping. The instance reports
///         <see cref="HostState.Draining"/> at once — that report is what takes it out of the rotation — and
///         goes on answering whatever still reaches it while the news travels. After the grace period it
///         reports <see cref="HostState.Drained"/> and stays up: <see cref="ExitMaintenanceCommand"/> puts it
///         back, a deploy stops it.
///     </para>
///     <para>
///         ⚠️ It neither enters maintenance mode nor stops the application. Maintenance mode would make every
///         request routed to it before the gateway hears answer 503 — the failed requests a drain exists to
///         avoid; stopping would make a drained instance impossible to put back.
///     </para>
/// </remarks>
public sealed class DrainCommandHandler(
    IHostStatus hostStatus,
    IControlPlane controlPlane,
    ILogger<DrainCommandHandler> logger) : IHostCommandHandler<DrainCommand>
{
    /// <summary>
    ///     Reports <see cref="HostState.Draining"/>, waits the command's grace period for in-flight requests,
    ///     then reports <see cref="HostState.Drained"/> — unless the instance was put back meanwhile.
    /// </summary>
    /// <param name="command">The drain command carrying the grace period.</param>
    /// <param name="ct">A token that cancels the grace-period wait; the instance then reports drained at once.</param>
    public async Task HandleAsync(DrainCommand command, CancellationToken ct = default)
    {
        logger.LogInformation("Drain requested with {GracePeriod}s grace period", command.GracePeriod.TotalSeconds);

        hostStatus.TransitionTo(HostState.Draining, "Leaving the rotation");
        await controlPlane.ReportStatusAsync(ct).ConfigureAwait(false);

        // SuppressThrowing: a cancelled grace period still ends in Drained below; Task.Delay's only awaited
        // failure is OperationCanceledException, so nothing else is hidden.
        await Task.Delay(command.GracePeriod, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // Put back (ExitMaintenanceCommand) while waiting: that decision is the later one.
        if (hostStatus.State != HostState.Draining)
            return;

        hostStatus.TransitionTo(HostState.Drained, "Out of the rotation, nothing in flight");
        await controlPlane.ReportStatusAsync(CancellationToken.None).ConfigureAwait(false);
        logger.LogInformation("Drained: out of the rotation and still running");
    }
}
