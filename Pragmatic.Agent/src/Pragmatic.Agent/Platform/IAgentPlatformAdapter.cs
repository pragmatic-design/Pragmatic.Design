namespace Pragmatic.Agent.Platform;

/// <summary>
///     Abstraction for platform-specific operations during deploy/maintenance.
///     Each adapter knows how to activate maintenance pages, stop apps, and report health
///     using the mechanisms native to its platform.
/// </summary>
internal interface IAgentPlatformAdapter
{
    /// <summary>Platform name for logging and CLI display.</summary>
    string PlatformName { get; }

    /// <summary>Activates the maintenance page so users see 503 while the app is down.</summary>
    Task ActivateMaintenanceAsync(string appId, CancellationToken ct = default);

    /// <summary>Deactivates the maintenance page, resuming normal traffic.</summary>
    Task DeactivateMaintenanceAsync(string appId, CancellationToken ct = default);

    /// <summary>Checks if the app process is currently running.</summary>
    Task<bool> IsAppRunningAsync(string appId, CancellationToken ct = default);

    /// <summary>Gracefully stops the app with a drain period.</summary>
    Task StopAppAsync(string appId, TimeSpan gracePeriod, CancellationToken ct = default);

    /// <summary>Reports health status to the platform's native health mechanism.</summary>
    Task ReportHealthAsync(string appId, HealthStatus status, CancellationToken ct = default);
}

/// <summary>Health status reported to the platform.</summary>
internal enum HealthStatus
{
    Healthy,
    Degraded,
    Unhealthy
}
