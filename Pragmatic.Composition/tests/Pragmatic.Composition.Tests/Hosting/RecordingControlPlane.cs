using Pragmatic.ControlPlane;
using Pragmatic.Maintenance;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     A control plane that records, at each <see cref="ReportStatusAsync" />, the host state and whether
///     maintenance mode was on — what the gateway would learn at that moment.
/// </summary>
public sealed class RecordingControlPlane(IHostStatus status, IMaintenanceMode? maintenance = null) : IControlPlane
{
    private readonly List<(HostState State, bool MaintenanceOn)> _reports = [];

    public IReadOnlyList<(HostState State, bool MaintenanceOn)> Reports => _reports;

    public bool IsConnected => true;

    public Task ReportStatusAsync(CancellationToken ct = default)
    {
        _reports.Add((status.State, maintenance?.IsActive ?? false));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HostInfo>> GetAllHostsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HostInfo>>([]);

    public async IAsyncEnumerable<ControlPlaneEvent> StreamEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    public Task<ControlPlaneError?> SendCommandAsync(string targetHostId, HostCommand command, CancellationToken ct = default)
        => Task.FromResult<ControlPlaneError?>(null);

    public Task BroadcastEventAsync(ControlPlaneEvent evt, CancellationToken ct = default) => Task.CompletedTask;
}
