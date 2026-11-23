using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     One Pragmatic Agent, started as the process it is in a deployment.
/// </summary>
/// <remarks>
///     <para>
///         A process and not an object in this one: the daemon's server, store and handler are internal
///         to its executable, so the only Agent a suite can run is the real one. Each has a socket, a data
///         directory and a gossip port of its own, which is what several machines would give several
///         Agents — here they share one machine, so the suite hands out the socket and the directory, and
///         each Agent chooses its gossip port and says which.
///     </para>
///     <para>
///         Ready when the daemon says so — the line it prints once it listens — rather than after a
///         delay: a daemon that fails to start ends the wait with its own output instead of a timeout.
///     </para>
/// </remarks>
internal sealed class AgentDaemon : IAsyncDisposable
{
    private const string ReadyLine = "Agent ready";

    /// <summary>What the daemon prints, before its ready line, with the gossip port it bound.</summary>
    private const string GossipPortLine = "Gossip port: ";

    private readonly Process _process;
    private readonly StringBuilder _output;

    private AgentDaemon(
        string name, string socketPath, int gossipPort, string dataDirectory, Process process, StringBuilder output)
    {
        Name = name;
        SocketPath = socketPath;
        GossipPort = gossipPort;
        DataDirectory = dataDirectory;
        _process = process;
        _output = output;
    }

    /// <summary>The UDP port this Agent gossips on — the one another Agent joins it at.</summary>
    public int GossipPort { get; }

    /// <summary>What the suite calls this Agent: the host it stands beside.</summary>
    public string Name { get; }

    /// <summary>The socket, or named pipe on Windows, a host connects to.</summary>
    public string SocketPath { get; }

    private string DataDirectory { get; }

    /// <summary>
    ///     Starts an Agent in the cluster that shares <paramref name="gossipKey" />, joining the Agent at
    ///     <paramref name="peer" /> when given, and returns once it listens.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The key is what makes the cluster: an Agent without one refuses every KV update it receives
    ///     over gossip, so each would keep what its own hosts wrote and nothing else.
    /// </remarks>
    public static async Task<AgentDaemon> StartAsync(
        string name, string gossipKey, AgentDaemon? peer = null, CancellationToken ct = default)
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "warehouse-agents", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);

        // A pipe name on Windows, a socket file elsewhere — the two transports the client speaks.
        var socketPath = OperatingSystem.IsWindows()
            ? $"warehouse-{name}-{Guid.NewGuid():N}"
            : Path.Combine(dataDirectory, "agent.sock");

        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Pragmatic.Agent.dll"));
        start.ArgumentList.Add("start");
        start.ArgumentList.Add("--socket");
        start.ArgumentList.Add(socketPath);
        start.ArgumentList.Add("--data-dir");
        start.ArgumentList.Add(dataDirectory);
        // On loopback, on a port the Agent chooses and reports: a port picked here would be free only until
        // another Agent, or another suite on the machine, took it before this one bound it. They find each
        // other through the peer they are given, at the port it reported, not by discovery.
        start.ArgumentList.Add("--bind");
        start.ArgumentList.Add("127.0.0.1");
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add("0");
        start.ArgumentList.Add("--gossip-key");
        start.ArgumentList.Add(gossipKey);
        if (peer is not null)
        {
            start.ArgumentList.Add("--peers");
            start.ArgumentList.Add($"127.0.0.1:{peer.GossipPort.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        var output = new StringBuilder();
        var gossipPort = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, line) =>
        {
            if (line.Data is null) return;
            lock (output) output.AppendLine(line.Data);
            if (line.Data.StartsWith(GossipPortLine, StringComparison.Ordinal))
            {
                var digits = line.Data.AsSpan(GossipPortLine.Length);
                var end = digits.IndexOf(' ');
                if (int.TryParse(end < 0 ? digits : digits[..end], NumberStyles.None, CultureInfo.InvariantCulture, out var port))
                    Volatile.Write(ref gossipPort, port);
            }

            if (line.Data.Contains(ReadyLine, StringComparison.Ordinal)) ready.TrySetResult();
        };
        process.ErrorDataReceived += (_, line) =>
        {
            if (line.Data is not null) lock (output) output.AppendLine(line.Data);
        };
        process.Exited += (_, _) => ready.TrySetException(new InvalidOperationException(
            $"The Agent '{name}' exited before it was ready:{Environment.NewLine}{Text(output)}"));

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"The Agent '{name}' did not say it was ready within 30s:{Environment.NewLine}{Text(output)}");
        }

        var reported = Volatile.Read(ref gossipPort);
        if (reported == 0)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException(
                $"The Agent '{name}' was ready without reporting the gossip port it bound:{Environment.NewLine}{Text(output)}");
        }

        return new AgentDaemon(name, socketPath, reported, dataDirectory, process, output);
    }

    /// <summary>What the daemon printed, for a failure message.</summary>
    public string Output => Text(_output);

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }

        _process.Dispose();

        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by the exiting daemon costs a temporary directory, not the run.
        }
    }

    private static string Text(StringBuilder output)
    {
        lock (output) return output.ToString();
    }
}
