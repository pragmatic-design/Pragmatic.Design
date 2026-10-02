using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pragmatic.Gateway.Samples;

/// <summary>
///     Demonstrates the operational surface: the health-check result shape, CLI argument overrides
///     applied on top of bound <see cref="GatewayOptions" />, the response-compression toggle, and
///     the graceful-shutdown drain timeout.
/// </summary>
internal static class OperationalSample
{
    public static void Run()
    {
        SampleConsole.Header("Operational — health, CLI overrides, compression, shutdown");

        // ── Health check result shape (GatewayHealthCheck reports Agent connectivity) ─────────
        SampleConsole.Section("Health check (/health) result shape");
        var healthy = BuildHealthResult(agentConnected: true);
        var degraded = BuildHealthResult(agentConnected: false);
        SampleConsole.Item("agent connected", $"{healthy.Status}: {healthy.Description}");
        SampleConsole.Item("agent disconnected", $"{degraded.Status}: {degraded.Description}");
        SampleConsole.Note("/health stays available even during maintenance (mapped before the maintenance middleware).");

        // ── CLI argument overrides (reproduces the Program.cs parsing loop) ───────────────────
        SampleConsole.Section("CLI argument overrides");
        var options = new GatewayOptions(); // as if bound from config
        SampleConsole.Item("before: HttpUrl", options.HttpUrl);
        SampleConsole.Item("before: HttpsUrl", options.HttpsUrl ?? "(disabled)");

        string[] cli =
        [
            "--listen", "http://*:9090",
            "--https", "https://*:9443",
            "--agent-socket", "/var/run/pragmatic/agent.sock",
            "--maintenance-page", "/etc/pragmatic/maintenance.html"
        ];
        ApplyCliOverrides(options, cli);

        SampleConsole.Item("after: HttpUrl", options.HttpUrl);
        SampleConsole.Item("after: HttpsUrl", options.HttpsUrl);
        SampleConsole.Item("after: AgentSocketPath", options.AgentSocketPath);
        SampleConsole.Item("after: MaintenancePagePath", options.MaintenancePagePath);

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

    // Mirrors the CLI override loop in Program.cs.
    private static void ApplyCliOverrides(GatewayOptions options, string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--agent-socket": options.AgentSocketPath = args[i + 1]; break;
                case "--listen": options.HttpUrl = args[i + 1]; break;
                case "--https": options.HttpsUrl = args[i + 1]; break;
                case "--maintenance-page": options.MaintenancePagePath = args[i + 1]; break;
            }
        }
    }
}
