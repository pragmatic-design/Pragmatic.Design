namespace Pragmatic.Agent.Platform;

/// <summary>
///     Azure Container Apps platform adapter (sidecar mode).
///     Similar to K8s — uses health probe for readiness.
///     Revision management is done via CLI (az containerapp update).
/// </summary>
internal sealed class AzureContainerAppsPlatformAdapter : IAgentPlatformAdapter
{
    public string PlatformName => "AzureContainerApps";

    private volatile bool _isReady = true;

    /// <summary>Whether the health probe should return healthy.</summary>
    public bool IsReady => _isReady;

    public Task ActivateMaintenanceAsync(string appId, CancellationToken ct = default)
    {
        _isReady = false; // ACA health probe fails → traffic stops
        AgentLogger.Info("ACA", $"Health probe set to NOT READY for {appId}");
        return Task.CompletedTask;
    }

    public Task DeactivateMaintenanceAsync(string appId, CancellationToken ct = default)
    {
        _isReady = true;
        AgentLogger.Info("ACA", $"Health probe set to READY for {appId}");
        return Task.CompletedTask;
    }

    public Task<bool> IsAppRunningAsync(string appId, CancellationToken ct = default)
        => Task.FromResult(true); // Sidecar mode — same container group

    public Task StopAppAsync(string appId, TimeSpan gracePeriod, CancellationToken ct = default)
    {
        _isReady = false;
        return Task.CompletedTask;
    }

    public Task ReportHealthAsync(string appId, HealthStatus status, CancellationToken ct = default)
    {
        _isReady = status == HealthStatus.Healthy;
        return Task.CompletedTask;
    }
}
