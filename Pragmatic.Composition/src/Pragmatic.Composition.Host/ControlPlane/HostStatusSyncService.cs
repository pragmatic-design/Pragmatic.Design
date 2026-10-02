using Microsoft.Extensions.Hosting;
using Pragmatic.ControlPlane;
using Pragmatic.Maintenance;

namespace Pragmatic.Composition.ControlPlane;

/// <summary>
///     Background service that synchronizes <see cref="IMaintenanceMode"/> state
///     with <see cref="IHostStatus"/> and transitions the host to Ready after startup.
/// </summary>
public sealed class HostStatusSyncService(
    IHostStatus hostStatus,
    IMaintenanceMode maintenanceMode) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        hostStatus.TransitionTo(HostState.Ready);

        while (!stoppingToken.IsCancellationRequested)
        {
            SyncMaintenanceState();
            // SuppressThrowing avoids the OperationCanceledException-on-shutdown noise that a
            // plain awaited Task.Delay produces; the loop's own IsCancellationRequested guard
            // then exits cleanly on the next iteration. Task.Delay has no other awaited failure
            // mode, so no real fault is being hidden — this is the standard BackgroundService
            // poll-loop shape.
            await Task.Delay(PollInterval, stoppingToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        hostStatus.TransitionTo(HostState.Stopped, "Process shutting down");
    }

    private void SyncMaintenanceState()
    {
        var currentState = hostStatus.State;

        if (maintenanceMode.IsActive && currentState is HostState.Ready)
            hostStatus.TransitionTo(HostState.Maintenance, maintenanceMode.Reason);
        else if (!maintenanceMode.IsActive && currentState is HostState.Maintenance)
            hostStatus.TransitionTo(HostState.Ready);
    }
}
