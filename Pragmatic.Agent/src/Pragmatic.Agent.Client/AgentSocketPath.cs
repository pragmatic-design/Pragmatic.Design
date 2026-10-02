namespace Pragmatic.Agent.Client;

/// <summary>
///     Single source of truth for the Agent socket name/path derived from an instance name. Both the
///     daemon (which binds it) and any client — the app host, the Gateway — must resolve the SAME path
///     for a given instance, so the convention lives here and is shared, never duplicated per caller.
/// </summary>
public static class AgentSocketPath
{
    /// <summary>
    ///     Resolves the socket path for an instance. <c>null</c>, empty, or <c>"default"</c> yield the
    ///     default instance path; any other name yields an instance-scoped path
    ///     (<c>pragmatic-agent-{instance}</c> pipe on Windows, <c>agent-{instance}.sock</c> on Unix).
    /// </summary>
    public static string Resolve(string? instance)
    {
        var name = string.IsNullOrWhiteSpace(instance) ? "default" : instance.Trim();

        if (OperatingSystem.IsWindows())
        {
            // Windows named pipe — the instance name is encoded in the pipe name.
            return name == "default" ? "pragmatic-agent" : $"pragmatic-agent-{name}";
        }

        // Linux/macOS Unix domain socket. Containers use /tmp (always writable) rather than /var/run.
        var baseDir = IsContainer() ? "/tmp/pragmatic" : "/var/run/pragmatic";
        return name == "default"
            ? Path.Combine(baseDir, "agent.sock")
            : Path.Combine(baseDir, $"agent-{name}.sock");
    }

    /// <summary>Whether the process runs inside a container (affects the Unix socket base directory).</summary>
    public static bool IsContainer()
    {
        if (File.Exists("/.dockerenv"))
            return true;
        if (Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST") is not null)
            return true;
        if (Environment.GetEnvironmentVariable("CONTAINER_APP_NAME") is not null)
            return true;
        if (Environment.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI_V4") is not null)
            return true;

        try
        {
            if (File.Exists("/proc/1/cgroup"))
            {
                var cgroup = File.ReadAllText("/proc/1/cgroup");
                if (cgroup.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
                    cgroup.Contains("kubepods", StringComparison.OrdinalIgnoreCase) ||
                    cgroup.Contains("containerd", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // Best effort — unreadable cgroup means "assume not a container".
        }

        return false;
    }
}
