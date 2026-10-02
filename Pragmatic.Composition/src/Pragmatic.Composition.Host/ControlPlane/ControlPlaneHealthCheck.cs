using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pragmatic.ControlPlane;

namespace Pragmatic.Composition.ControlPlane;

/// <summary>
///     ASP.NET Core health check that bridges <see cref="IHostStatus"/> and
///     <see cref="IHostHealthAggregator"/> into the standard health check endpoint.
///     Reports both the host lifecycle state and contributor health.
/// </summary>
public sealed class ControlPlaneHealthCheck(
    IHostStatus hostStatus,
    IHostHealthAggregator? healthAggregator = null) : IHealthCheck
{
    /// <summary>
    ///     Evaluates host health by combining the host lifecycle state with aggregated contributor health,
    ///     reporting the worst of the two.
    /// </summary>
    /// <param name="context">The health check context supplied by the ASP.NET Core health check system.</param>
    /// <param name="ct">A token to cancel the health evaluation.</param>
    /// <returns>
    ///     An unhealthy result while the host is Starting or Stopped; otherwise a result reflecting the
    ///     worst of the host lifecycle state and contributor statuses.
    /// </returns>
    /// <remarks>
    ///     Combines statuses via <c>Math.Max</c> on the enum ordinals, which assumes
    ///     <c>ContributorHealthStatus</c> is declared in ascending severity order
    ///     (Healthy &lt; Degraded &lt; Unhealthy). Preserve that ordering if the enum is extended.
    /// </remarks>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        // Host lifecycle state takes priority
        if (hostStatus.State is HostState.Starting or HostState.Stopped)
            return HealthCheckResult.Unhealthy($"Host state: {hostStatus.State}");

        var data = new Dictionary<string, object>
        {
            ["hostState"] = hostStatus.State.ToString(),
        };

        if (hostStatus.StateReason is not null)
            data["stateReason"] = hostStatus.StateReason;

        if (hostStatus.MigrationStatus is not null)
        {
            data["migrationDatabase"] = hostStatus.MigrationStatus.DatabaseName;
            data["migrationProgress"] = $"{hostStatus.MigrationStatus.ProgressPercent:F0}%";
        }

        // Aggregate contributor health
        var contributorStatus = ContributorHealthStatus.Healthy;
        if (healthAggregator is not null)
        {
            var report = await healthAggregator.GetReportAsync(ct).ConfigureAwait(false);
            contributorStatus = report.OverallStatus;

            foreach (var (name, contributorReport) in report.Contributors)
            {
                data[$"contributor.{name}"] = contributorReport.Status.ToString();
                if (contributorReport.Message is not null)
                    data[$"contributor.{name}.message"] = contributorReport.Message;
            }
        }

        // Combine: host lifecycle state + contributor health → worst wins
        var hostHealthStatus = hostStatus.State switch
        {
            HostState.Ready => ContributorHealthStatus.Healthy,
            HostState.Maintenance or HostState.Migrating or HostState.Draining or HostState.Drained
                => ContributorHealthStatus.Degraded,
            _ => ContributorHealthStatus.Unhealthy,
        };

        // ASSUMPTION: ContributorHealthStatus ordinal order equals severity order
        // (Healthy=0 < Degraded=1 < Unhealthy=2). Must be maintained if enum is extended.
        var worst = (ContributorHealthStatus)Math.Max((int)hostHealthStatus, (int)contributorStatus);

        return worst switch
        {
            ContributorHealthStatus.Healthy => HealthCheckResult.Healthy("Host is ready", data),
            ContributorHealthStatus.Degraded => HealthCheckResult.Degraded(
                hostStatus.StateReason ?? "One or more components degraded", data: data),
            _ => HealthCheckResult.Unhealthy(
                hostStatus.StateReason ?? "One or more components unhealthy", data: data),
        };
    }
}
