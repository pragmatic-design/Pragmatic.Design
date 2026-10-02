namespace Pragmatic.Agent.Configuration;

/// <summary>
///     Resolves all file/socket paths for an Agent instance.
///     Instance-aware: multiple agents on the same host use different paths based on instance name.
///     Environment-aware: adapts to Windows, Linux, macOS, Docker, Kubernetes.
/// </summary>
internal sealed class AgentPaths
{
    /// <summary>
    ///     Instance name — disambiguates multiple agents on the same host.
    ///     Default: "default". Override with --instance or PRAGMATIC_AGENT_INSTANCE env var.
    ///     Examples: "default", "booking", "billing", "gateway".
    /// </summary>
    public string InstanceName { get; }

    /// <summary>Socket/pipe path for app↔agent IPC.</summary>
    public string SocketPath { get; }

    /// <summary>Directory for KV persistence (kv.json).</summary>
    public string DataDirectory { get; }

    /// <summary>Full path to the KV persistence file.</summary>
    public string KvFilePath { get; }

    /// <summary>Full path to the agent ID persistence file.</summary>
    public string AgentIdFilePath { get; }

    public AgentPaths(string? instanceName = null, string? socketPathOverride = null, string? dataDirOverride = null)
    {
        InstanceName = instanceName
            ?? Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_INSTANCE")
            ?? "default";

        SocketPath = socketPathOverride ?? ResolveSocketPath(InstanceName);
        DataDirectory = dataDirOverride ?? ResolveDataDirectory(InstanceName);
        KvFilePath = Path.Combine(DataDirectory, "kv.json");
        AgentIdFilePath = Path.Combine(DataDirectory, "agent-id");
    }

    /// <summary>
    ///     Ensures all directories exist.
    /// </summary>
    public void EnsureDirectories()
    {
        if (!Directory.Exists(DataDirectory))
            Directory.CreateDirectory(DataDirectory);

        // For Unix sockets, ensure parent directory exists
        if (!OperatingSystem.IsWindows())
        {
            var socketDir = Path.GetDirectoryName(SocketPath);
            if (socketDir is not null && !Directory.Exists(socketDir))
                Directory.CreateDirectory(socketDir);
        }
    }

    /// <summary>
    ///     Gets or creates a persistent Agent ID.
    ///     Persisted to disk so it survives restarts — other agents see the same member.
    /// </summary>
    // Static lock prevents TOCTOU when two agent processes start concurrently on the same host
    // and both reach the File.Exists / File.WriteAllText pair before either has committed.
    private static readonly object _agentIdLock = new();

    public string GetOrCreateAgentId()
    {
        lock (_agentIdLock)
        {
            // Re-check inside the lock: another process may have created the file between our
            // initial check and acquiring the lock.  On most OSes file-create is not atomic
            // across processes, so we use a try-exclusive-create pattern instead.
            if (File.Exists(AgentIdFilePath))
            {
                var id = File.ReadAllText(AgentIdFilePath).Trim();
                if (!string.IsNullOrEmpty(id))
                    return id;
            }

            var newId = $"agent-{Environment.MachineName}-{InstanceName}-{Guid.NewGuid().ToString("N")[..8]}";

            EnsureDirectories();

            // Write to temp file then atomically rename so readers never see a partial write.
            var tmpPath = AgentIdFilePath + ".tmp";
            File.WriteAllText(tmpPath, newId);
            File.Move(tmpPath, AgentIdFilePath, overwrite: false);

            return newId;
        }
    }

    // Single source of truth shared with clients (Gateway, app host) so every party resolves the same
    // socket for a given instance. See Pragmatic.Agent.Client.AgentSocketPath.
    private static string ResolveSocketPath(string instance)
        => Client.AgentSocketPath.Resolve(instance);

    private static string ResolveDataDirectory(string instance)
    {
        // Environment override (useful for containers with mounted volumes)
        var envDir = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_DATA");
        if (!string.IsNullOrEmpty(envDir))
            return instance == "default" ? envDir : Path.Combine(envDir, instance);

        if (IsContainer())
        {
            // Container: /data/pragmatic-agent (expects volume mount)
            // Falls back to /tmp if /data doesn't exist
            var containerData = Directory.Exists("/data")
                ? "/data/pragmatic-agent"
                : "/tmp/pragmatic-agent";

            return instance == "default" ? containerData : Path.Combine(containerData, instance);
        }

        // Host: platform-standard data directory
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrEmpty(appData))
        {
            // Fallback for minimal Linux (no XDG)
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share");
        }

        var baseDir = Path.Combine(appData, "pragmatic-agent");

        return instance == "default" ? baseDir : Path.Combine(baseDir, instance);
    }

    private static bool IsContainer() => Client.AgentSocketPath.IsContainer();

    public override string ToString() => $"""
        Instance: {InstanceName}
        Socket:   {SocketPath}
        Data:     {DataDirectory}
        KV:       {KvFilePath}
        AgentId:  {AgentIdFilePath}
        Container: {IsContainer()}
        """;
}
