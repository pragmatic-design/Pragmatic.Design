namespace Pragmatic.Agent.Platform;

/// <summary>
///     IIS platform adapter. Uses app_offline.htm for maintenance pages
///     and appcmd for app pool management.
/// </summary>
internal sealed class IisPlatformAdapter : IAgentPlatformAdapter
{
    public string PlatformName => "IIS";

    public Task ActivateMaintenanceAsync(string appId, CancellationToken ct = default)
    {
        // IIS automatically serves app_offline.htm and stops the app pool
        var sitePath = ResolveSitePath(appId);
        var offlinePath = Path.Combine(sitePath, "app_offline.htm");

        File.WriteAllText(offlinePath, DefaultMaintenancePage);
        AgentLogger.Info("IIS", $"Wrote app_offline.htm for {appId}");
        return Task.CompletedTask;
    }

    public Task DeactivateMaintenanceAsync(string appId, CancellationToken ct = default)
    {
        var sitePath = ResolveSitePath(appId);
        var offlinePath = Path.Combine(sitePath, "app_offline.htm");

        if (File.Exists(offlinePath))
        {
            File.Delete(offlinePath);
            AgentLogger.Info("IIS", $"Removed app_offline.htm for {appId}");
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsAppRunningAsync(string appId, CancellationToken ct = default)
    {
        var sitePath = ResolveSitePath(appId);
        var offlinePath = Path.Combine(sitePath, "app_offline.htm");
        return Task.FromResult(!File.Exists(offlinePath));
    }

    public async Task StopAppAsync(string appId, TimeSpan gracePeriod, CancellationToken ct = default)
    {
        // IIS: app_offline.htm triggers graceful shutdown
        await ActivateMaintenanceAsync(appId, ct).ConfigureAwait(false);
        await Task.Delay(gracePeriod, ct).ConfigureAwait(false);
    }

    public Task ReportHealthAsync(string appId, HealthStatus status, CancellationToken ct = default)
    {
        // IIS monitors health via its own mechanisms — no action needed
        return Task.CompletedTask;
    }

    private static string ResolveSitePath(string appId)
    {
        // Security: appId is caller-supplied and flows into a filesystem path and an env-var name.
        // Reject anything that could traverse out of the intended site root (path separators, "..",
        // drive/UNC roots, control chars) before it is used.
        var safeAppId = ValidateAppId(appId);

        // Convention: site path from environment or default IIS location
        var customPath = Environment.GetEnvironmentVariable($"PRAGMATIC_IIS_PATH_{safeAppId.ToUpperInvariant()}");
        if (customPath is not null)
            return customPath;

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Pragmatic");
        var combined = Path.GetFullPath(Path.Combine(root, safeAppId));

        // Defense-in-depth: ensure the resolved path is still under the intended root.
        var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Resolved site path escapes the application root.", nameof(appId));

        return combined;
    }

    /// <summary>
    ///     Validates a caller-supplied app identifier intended for use in a path segment.
    ///     Allows only a conservative character set; rejects separators, traversal, and roots.
    /// </summary>
    private static string ValidateAppId(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("appId must not be empty.", nameof(appId));

        if (appId is "." or ".."
            || appId.Contains("..", StringComparison.Ordinal)
            || appId.IndexOfAny(['/', '\\', ':']) >= 0
            || appId.IndexOf('\0') >= 0
            || appId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"Invalid appId '{appId}': must be a single path-safe segment.", nameof(appId));
        }

        return appId;
    }

    private const string DefaultMaintenancePage = """
        <!DOCTYPE html>
        <html><head><title>Maintenance</title>
        <meta http-equiv="refresh" content="30">
        <style>body{font-family:system-ui;display:flex;justify-content:center;align-items:center;min-height:100vh;margin:0;background:#f8f9fa;color:#343a40}.c{text-align:center}h1{font-size:2rem}p{color:#6c757d}</style>
        </head><body><div class="c"><h1>Under Maintenance</h1><p>We'll be back shortly.</p></div></body></html>
        """;
}
