using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Agent.Client;

/// <summary>
///     Auto-starts the Agent daemon in Development mode if it's not already running.
///     Looks for the Agent binary in the solution build output or PATH.
///     The process is stopped when the app shuts down.
/// </summary>
internal sealed class AgentAutoStart(ILogger? logger = null) : IDisposable
{
    private Process? _agentProcess;

    /// <summary>
    ///     Starts the Agent daemon if it's not already listening.
    ///     Returns true if the Agent was started, false if it was already running.
    /// </summary>
    public bool StartIfNeeded(AgentOptions options)
    {
        // Only auto-start in Development
        if (!IsDevelopment())
            return false;

        // Check if Agent is already listening
        if (IsAgentRunning(options.SocketPath))
        {
            logger?.LogDebug("Agent already running at {SocketPath}", options.SocketPath);
            return false;
        }

        var agentPath = FindAgentBinary();
        if (agentPath is null)
        {
            logger?.LogWarning("Agent binary not found — cannot auto-start. Run the Agent daemon manually.");
            return false;
        }

        logger?.LogInformation("Auto-starting Agent daemon: {AgentPath}", agentPath);

        _agentProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{agentPath}\" -- start --socket \"{options.SocketPath}\"",
            UseShellExecute = false,
            // Do NOT redirect stdout/stderr without a consumer — the pipe buffer fills up
            // (typically ~4 KB on Linux) causing the child process to block on write,
            // which deadlocks WaitForExit on the parent side.
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true
        });

        if (_agentProcess is null)
        {
            logger?.LogError("Failed to start Agent daemon");
            return false;
        }

        // Poll for the socket to appear rather than using a fixed sleep.
        // This avoids blocking a thread (Thread.Sleep) on an async startup path and
        // prevents both under-waiting (agent not ready) and over-waiting (slow machines).
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (IsAgentRunning(options.SocketPath))
                break;
            // Use a short Task.Delay equivalent via spinning; we are already in a sync context.
            System.Threading.Thread.Sleep(50);
        }

        logger?.LogInformation("Agent daemon started (PID {Pid})", _agentProcess.Id);
        return true;
    }

    public void Dispose()
    {
        if (_agentProcess is not null && !_agentProcess.HasExited)
        {
            logger?.LogInformation("Stopping Agent daemon (PID {Pid})", _agentProcess.Id);
            try
            {
                _agentProcess.Kill(entireProcessTree: true);
                _agentProcess.WaitForExit(3000);
            }
            catch { /* Best effort */ }

            _agentProcess.Dispose();
            _agentProcess = null;
        }
    }

    private static bool IsDevelopment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
               ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAgentRunning(string socketPath)
    {
        // Probe the endpoint by actually connecting — a stale socket file or orphaned pipe name
        // left by a crashed agent would pass a mere existence check and falsely suppress
        // auto-start. Only a successful connect proves the daemon is alive and accepting.
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var pipeName = Path.GetFileName(socketPath);

                // Cheap negative check first: no pipe object → nothing to connect to.
                if (!File.Exists($@"\\.\pipe\{pipeName}"))
                    return false;

                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                pipe.Connect(250);
                return pipe.IsConnected;
            }

            // Cheap negative check first: no socket file → nothing to connect to.
            if (!File.Exists(socketPath))
                return false;

            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
            return socket.Connected;
        }
        catch
        {
            // Connection refused / timeout / stale endpoint → agent is not actually running.
            return false;
        }
    }

    /// <summary>
    ///     Finds the Agent project in the solution.
    ///     Walks up from the current directory looking for Pragmatic.Agent.csproj.
    /// </summary>
    private static string? FindAgentBinary()
    {
        var current = AppContext.BaseDirectory;

        // Walk up looking for the solution root (contains Pragmatic.Agent/)
        for (var i = 0; i < 10; i++)
        {
            var parent = Path.GetDirectoryName(current);
            if (parent is null) break;

            var agentProject = Path.Combine(parent, "Pragmatic.Agent", "src", "Pragmatic.Agent", "Pragmatic.Agent.csproj");
            if (File.Exists(agentProject))
                return agentProject;

            current = parent;
        }

        return null;
    }
}
