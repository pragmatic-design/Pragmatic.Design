using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pragmatic.Gateway.Samples;

/// <summary>
///     Demonstrates the operational surface: the health-check result shape, the response-compression
///     toggle, and the graceful-shutdown drain timeout.
/// </summary>
/// <remarks>
///     The command-line overrides (<c>--listen</c>, <c>--agent-socket</c>, …) are not reproduced here:
///     <c>Program.cs</c> of the Gateway is where they are parsed, and the README shows them. A copy of
///     that loop in this small executable made Bitdefender quarantine it as
///     <c>Gen:Variant.MSILHeracles</c>, which stops the gate on a Windows machine running it.
/// </remarks>
internal static class OperationalSample
{
    public static void Run()
    {
        SampleConsole.Header("Operational — health, compression, shutdown");

        // ── Health check result shape (GatewayHealthCheck reports Agent connectivity) ─────────
        SampleConsole.Section("Health check (/health) result shape");
        var healthy = BuildHealthResult(agentConnected: true);
        var degraded = BuildHealthResult(agentConnected: false);
        SampleConsole.Item("agent connected", $"{healthy.Status}: {healthy.Description}");
        SampleConsole.Item("agent disconnected", $"{degraded.Status}: {degraded.Description}");
        SampleConsole.Note("/health stays available even during maintenance (mapped before the maintenance middleware).");

        var options = new GatewayOptions(); // as if bound from config

        // ── Response compression + graceful shutdown ─────────────────────────────────────────
        SampleConsole.Section("Compression & graceful shutdown");
        SampleConsole.Item("EnableCompression", options.EnableCompression);
        SampleConsole.Item("EnableTelemetry", options.EnableTelemetry);
        SampleConsole.Item("ShutdownTimeout", options.ShutdownTimeout);
        SampleConsole.Note("ShutdownTimeout flows into HostOptions.ShutdownTimeout to drain in-flight requests on SIGTERM.");
    }

    // Mirrors GatewayHealthCheck.CheckHealthAsync.
    private static HealthCheckResult BuildHealthResult(bool agentConnected)
    {
        var data = new Dictionary<string, object>
        {
            ["agent_connected"] = agentConnected,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
        };

        return agentConnected
            ? HealthCheckResult.Healthy("Gateway operational", data)
            : HealthCheckResult.Degraded("Agent disconnected — using cached/static routes", data: data);
    }
}
