using Microsoft.Extensions.Logging;
using Pragmatic.Composition.Hosting;
using Pragmatic.ControlPlane;
using Pragmatic.Maintenance;

namespace Pragmatic.Composition.ControlPlane;

/// <summary>
///     Handles <see cref="EnterMaintenanceCommand"/> — activates maintenance mode.
///     The maintenance handle is stored in the injected <see cref="MaintenanceHandleHolder"/>
///     so it can be deactivated by <see cref="ExitMaintenanceCommandHandler"/>.
/// </summary>
/// <remarks>
///     The handler is registered on every generated host, so this is the door through which an
///     operator drains traffic from the outside. It only opens for a host that asked for it:
///     <see cref="MaintenanceModeOptions.EnableRuntimeMaintenance"/>, which
///     <c>UseMaintenanceMode()</c> sets. A host that never opted in refuses the command and says so,
///     rather than going quiet on a mode it has no page for.
/// </remarks>
public sealed class MaintenanceCommandHandler(
    IMaintenanceMode maintenanceMode,
    MaintenanceHandleHolder handleHolder,
    MaintenanceModeOptions options,
    ILogger<MaintenanceCommandHandler> logger) : IHostCommandHandler<EnterMaintenanceCommand>
{
    /// <summary>
    ///     Activates maintenance mode and stores the handle in the <see cref="MaintenanceHandleHolder" />.
    ///     Idempotent: if maintenance is already active (including a concurrent enter) the new handle is
    ///     disposed and the command is ignored. Refused outright when runtime maintenance is not enabled.
    /// </summary>
    /// <param name="command">The enter-maintenance command carrying the reason and optional ETA.</param>
    /// <param name="ct">A cancellation token (unused; handling is synchronous).</param>
    /// <returns>A completed task.</returns>
    public Task HandleAsync(EnterMaintenanceCommand command, CancellationToken ct = default)
    {
        if (!options.EnableRuntimeMaintenance)
        {
            logger.LogWarning(
                "Refusing EnterMaintenanceCommand: runtime maintenance is not enabled on this host. "
                + "Call UseMaintenanceMode() to opt in. Reason was: {Reason}", command.Reason);
            return Task.CompletedTask;
        }

        if (maintenanceMode.IsActive)
        {
            logger.LogInformation("Maintenance mode already active, ignoring EnterMaintenanceCommand");
            return Task.CompletedTask;
        }

        var handle = maintenanceMode.Activate(command.Reason, command.Eta);
        if (!handleHolder.TrySet(handle))
        {
            // Another concurrent enter sneaked in — dispose the one we just got
            handle.Dispose();
            logger.LogInformation("Maintenance mode already active (concurrent), ignoring EnterMaintenanceCommand");
        }
        else
        {
            logger.LogInformation("Maintenance mode activated via control plane: {Reason}", command.Reason);
        }

        return Task.CompletedTask;
    }
}
