using System.Diagnostics;

namespace Pragmatic.Agent.Platform;

/// <summary>
///     nginx platform adapter (Ubuntu/Linux). Manages maintenance mode by writing
///     an nginx config snippet and reloading. Uses systemctl for app lifecycle.
/// </summary>
internal sealed class NginxPlatformAdapter : IAgentPlatformAdapter
{
    public string PlatformName => "nginx";

    private readonly string _maintenanceConfigPath = "/etc/nginx/conf.d/pragmatic-maintenance.conf";

    public async Task ActivateMaintenanceAsync(string appId, CancellationToken ct = default)
    {
        // Write nginx config that returns 503 for the app's routes
        var config = $$"""
            # Pragmatic Agent: maintenance mode for {{appId}}
            # Auto-generated — will be removed when maintenance ends
            server {
                listen 80 default_server;
                return 503;
                error_page 503 @maintenance;
                location @maintenance {
                    root /var/www/pragmatic;
                    try_files /maintenance.html =503;
                }
            }
            """;

        await File.WriteAllTextAsync(_maintenanceConfigPath, config, ct).ConfigureAwait(false);
        await ReloadNginxAsync(ct).ConfigureAwait(false);
        AgentLogger.Info("nginx", $"Maintenance activated for {appId}");
    }

    public async Task DeactivateMaintenanceAsync(string appId, CancellationToken ct = default)
    {
        if (File.Exists(_maintenanceConfigPath))
        {
            File.Delete(_maintenanceConfigPath);
            await ReloadNginxAsync(ct).ConfigureAwait(false);
            AgentLogger.Info("nginx", $"Maintenance deactivated for {appId}");
        }
    }

    public Task<bool> IsAppRunningAsync(string appId, CancellationToken ct = default)
    {
        // Check if systemd service is active
        return RunCommandAsync(ct, "systemctl", "is-active", "--quiet", appId);
    }

    public async Task StopAppAsync(string appId, TimeSpan gracePeriod, CancellationToken ct = default)
    {
        await RunCommandAsync(ct, "systemctl", "stop", appId).ConfigureAwait(false);
    }

    public Task ReportHealthAsync(string appId, HealthStatus status, CancellationToken ct = default)
    {
        // nginx doesn't have a native health reporting mechanism
        // Health is inferred from upstream availability
        return Task.CompletedTask;
    }

    private static async Task ReloadNginxAsync(CancellationToken ct)
    {
        await RunCommandAsync(ct, "nginx", "-s", "reload").ConfigureAwait(false);
    }

    // Args are passed as a list so ProcessStartInfo escapes each one individually.
    // Never build a single Arguments string from user input — it opens command injection
    // (e.g. appId = "foo; reboot" would be appended to systemctl verbatim).
    private static async Task<bool> RunCommandAsync(CancellationToken ct, string command, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var process = Process.Start(psi);
            if (process is null) return false;
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
