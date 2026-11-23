using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Pragmatic.Agent.Tests.Gossip;

/// <summary>
///     One Agent daemon, run as the executable it is in a deployment, on a gossip port it chose itself.
/// </summary>
/// <remarks>
///     Started with <c>--port 0</c>: the port is the one the daemon says it bound, read from its output
///     before its ready line. A port the test chose would be free when chosen and taken by anybody
///     before the daemon binds it.
/// </remarks>
internal sealed class AgentProcess : IAsyncDisposable
{
    private const string ReadyLine = "Agent ready";
    private const string GossipPortLine = "Gossip port: ";

    private readonly Process _process;
    private readonly StringBuilder _output;
    private readonly string _dataDirectory;

    private AgentProcess(int gossipPort, string socketPath, string dataDirectory, Process process, StringBuilder output)
    {
        GossipPort = gossipPort;
        SocketPath = socketPath;
        _dataDirectory = dataDirectory;
        _process = process;
        _output = output;
    }

    /// <summary>The UDP port the daemon reported it bound.</summary>
    public int GossipPort { get; }

    /// <summary>The socket, or named pipe on Windows, a client connects to.</summary>
    public string SocketPath { get; }

    /// <summary>What the daemon printed, for a failure message.</summary>
    public string Output => Text(_output);

    /// <summary>Starts a daemon on an ephemeral gossip port, joining <paramref name="peer" /> when given.</summary>
    public static async Task<AgentProcess> StartAsync(string gossipKey, AgentProcess? peer = null)
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "agent-process-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        var socketPath = OperatingSystem.IsWindows()
            ? $"agent-process-{Guid.NewGuid():N}"
            : Path.Combine(dataDirectory, "agent.sock");

        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        // The runtime configuration and dependencies of this test assembly, not the Agent's copies beside it:
        // the fuzz harness this project also references targets net10.0 only and carries the net10.0 Agent's
        // runtimeconfig.json, which can land in the net11.0 output and start a net11.0 build on the wrong runtime.
        // This assembly's own files always describe the target it runs on, and they list the Agent too.
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Pragmatic.Agent.Tests.runtimeconfig.json"));
        start.ArgumentList.Add("--depsfile");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Pragmatic.Agent.Tests.deps.json"));
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Pragmatic.Agent.dll"));
        start.ArgumentList.Add("start");
        start.ArgumentList.Add("--socket");
        start.ArgumentList.Add(socketPath);
        start.ArgumentList.Add("--data-dir");
        start.ArgumentList.Add(dataDirectory);
        start.ArgumentList.Add("--bind");
        start.ArgumentList.Add("127.0.0.1");
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add("0");
        start.ArgumentList.Add("--gossip-key");
        start.ArgumentList.Add(gossipKey);
        if (peer is not null)
        {
            start.ArgumentList.Add("--peers");
            start.ArgumentList.Add($"127.0.0.1:{peer.GossipPort.ToString(CultureInfo.InvariantCulture)}");
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
            $"The Agent exited before it was ready:{Environment.NewLine}{Text(output)}"));

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The Agent did not say it was ready within 30s:{Environment.NewLine}{Text(output)}");
        }

        var reported = Volatile.Read(ref gossipPort);
        if (reported == 0)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException(
                $"The Agent was ready without reporting the gossip port it bound:{Environment.NewLine}{Text(output)}");
        }

        return new AgentProcess(reported, socketPath, dataDirectory, process, output);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();

        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
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
