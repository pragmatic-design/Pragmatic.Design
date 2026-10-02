namespace Pragmatic.Agent.Platform;

/// <summary>
///     AWS ECS/Fargate platform adapter (sidecar mode).
///     Uses ECS health check endpoint for service discovery.
///     Task lifecycle managed by ECS service scheduler.
/// </summary>
internal sealed class AwsEcsPlatformAdapter : IAgentPlatformAdapter
{
    public string PlatformName => "AWS-ECS";

    private volatile bool _isReady = true;

    /// <summary>Whether the ECS health check should return healthy.</summary>
    public bool IsReady => _isReady;

    public Task ActivateMaintenanceAsync(string appId, CancellationToken ct = default)
    {
        _isReady = false; // ALB health check fails → task drained
        AgentLogger.Info("ECS", $"Health check set to UNHEALTHY for {appId}");
        return Task.CompletedTask;
    }

    public Task DeactivateMaintenanceAsync(string appId, CancellationToken ct = default)
    {
        _isReady = true;
        AgentLogger.Info("ECS", $"Health check set to HEALTHY for {appId}");
        return Task.CompletedTask;
    }

    public Task<bool> IsAppRunningAsync(string appId, CancellationToken ct = default)
        => Task.FromResult(true); // Sidecar — same task

    public Task StopAppAsync(string appId, TimeSpan gracePeriod, CancellationToken ct = default)
    {
        _isReady = false; // ECS will drain and replace the task
        return Task.CompletedTask;
    }

    public Task ReportHealthAsync(string appId, HealthStatus status, CancellationToken ct = default)
    {
        _isReady = status == HealthStatus.Healthy;
        return Task.CompletedTask;
    }
}
