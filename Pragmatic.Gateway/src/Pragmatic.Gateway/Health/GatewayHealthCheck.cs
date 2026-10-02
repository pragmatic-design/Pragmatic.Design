using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pragmatic.Agent.Client;

namespace Pragmatic.Gateway.Health;

/// <summary>
///     Health check that reports Gateway + Agent connection status.
/// </summary>
internal sealed class GatewayHealthCheck(AgentConnection agent) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var data = new Dictionary<string, object>
        {
            ["agent_connected"] = agent.IsConnected,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
        };

        if (agent.IsConnected)
            return Task.FromResult(HealthCheckResult.Healthy("Gateway operational", data));

        return Task.FromResult(HealthCheckResult.Degraded("Agent disconnected — using cached/static routes", data: data));
    }
}
