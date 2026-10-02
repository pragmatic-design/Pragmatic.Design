namespace Pragmatic.Agent.Platform;

/// <summary>
///     Kubernetes platform adapter (sidecar mode).
///     Maintenance is signaled via readiness probe failure — K8s stops routing traffic.
///     Pod lifecycle is managed by K8s, not the Agent.
/// </summary>
internal sealed class KubernetesPlatformAdapter : IAgentPlatformAdapter
{
    public string PlatformName => "Kubernetes";

    // Readiness state — the Agent exposes a health endpoint that K8s probes
    private volatile bool _isReady = true;

    /// <summary>Whether the readiness probe should return healthy.</summary>
    public bool IsReady => _isReady;

    public Task ActivateMaintenanceAsync(string appId, CancellationToken ct = default)
    {
        _isReady = false; // K8s readiness probe will fail → traffic stops
        AgentLogger.Info("K8s", $"Readiness probe set to NOT READY for {appId}");
        return Task.CompletedTask;
    }

    public Task DeactivateMaintenanceAsync(string appId, CancellationToken ct = default)
    {
        _isReady = true; // K8s readiness probe will pass → traffic resumes
        AgentLogger.Info("K8s", $"Readiness probe set to READY for {appId}");
        return Task.CompletedTask;
    }

    public Task<bool> IsAppRunningAsync(string appId, CancellationToken ct = default)
    {
        // In K8s sidecar mode, if we're running, the app is running (same pod)
        return Task.FromResult(true);
    }

    public Task StopAppAsync(string appId, TimeSpan gracePeriod, CancellationToken ct = default)
    {
        // K8s manages pod lifecycle — we signal via readiness probe
        // PreStop hook or SIGTERM handles graceful shutdown
        _isReady = false;
        return Task.CompletedTask;
    }

    public Task ReportHealthAsync(string appId, HealthStatus status, CancellationToken ct = default)
    {
        _isReady = status == HealthStatus.Healthy;
        return Task.CompletedTask;
    }
}
