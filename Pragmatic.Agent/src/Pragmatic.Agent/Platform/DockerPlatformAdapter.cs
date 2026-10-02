using System.Diagnostics;

namespace Pragmatic.Agent.Platform;

/// <summary>
///     Docker platform adapter. Uses docker CLI for container lifecycle.
///     Maintenance page is served by the Pragmatic Gateway (separate container).
/// </summary>
internal sealed class DockerPlatformAdapter : IAgentPlatformAdapter
{
    public string PlatformName => "Docker";

    public Task ActivateMaintenanceAsync(string appId, CancellationToken ct = default)
    {
        // In Docker, maintenance page is served by the Gateway container.
        // The Agent signals maintenance via KV store, Gateway picks it up.
        AgentLogger.Info("Docker", $"Maintenance signaled for {appId} (Gateway will serve 503)");
        return Task.CompletedTask;
    }

    public Task DeactivateMaintenanceAsync(string appId, CancellationToken ct = default)
    {
        AgentLogger.Info("Docker", $"Maintenance cleared for {appId}");
        return Task.CompletedTask;
    }

    public async Task<bool> IsAppRunningAsync(string appId, CancellationToken ct = default)
    {
        return await RunDockerAsync(ct, "inspect", "-f", "{{.State.Running}}", appId).ConfigureAwait(false);
    }

    public async Task StopAppAsync(string appId, TimeSpan gracePeriod, CancellationToken ct = default)
    {
        var seconds = ((int)gracePeriod.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await RunDockerAsync(ct, "stop", "-t", seconds, appId).ConfigureAwait(false);
    }

    public Task ReportHealthAsync(string appId, HealthStatus status, CancellationToken ct = default)
    {
        // Docker health checks are configured in Dockerfile HEALTHCHECK
        // The Agent's health endpoint serves as the check target
        return Task.CompletedTask;
    }

    // Args are passed as a list so ProcessStartInfo escapes each one individually.
    // Never build a single Arguments string from user input — it opens command injection
    // (e.g. appId = "x; rm -rf /" would break out of the docker invocation).
    private static async Task<bool> RunDockerAsync(CancellationToken ct, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                // Do NOT redirect stdout/stderr without consuming the streams — the pipe buffer
                // (typically ~4 KB on Linux) fills up when docker writes output and causes
                // WaitForExitAsync to deadlock.
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var process = Process.Start(psi);
            if (process is null) return false;
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return false;
        }
    }
}
