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
    private readonly Task _exited;
    private readonly StringBuilder _output;
    private readonly string _dataDirectory;

    private AgentProcess(int gossipPort, string socketPath, string dataDirectory, Process process, Task exited, StringBuilder output)
    {
        GossipPort = gossipPort;
        SocketPath = socketPath;
        _dataDirectory = dataDirectory;
        _process = process;
        _exited = exited;
        _output = output;
    }

    /// <summary>The UDP port the daemon reported it bound.</summary>
    public int GossipPort { get; }

    /// <summary>The socket, or named pipe on Windows, a client connects to.</summary>
    public string SocketPath { get; }

    /// <summary>What the daemon printed, for a failure message.</summary>
    public string Output => Text(_output);

    /// <summary>Whether the daemon's process has exited; still readable once this is disposed.</summary>
    public bool HasExited => _exited.IsCompletedSuccessfully;

    /// <summary>
    ///     Awaits daemons started at once; when one fails to start, stops every one that did before
    ///     the failure propagates.
    /// </summary>
    /// <remarks>
    ///     A daemon nobody disposes outlives the test host and holds the pipes it inherited from it, and
    ///     the gate waits for those to close: <c>await using var a = await x; await using var b = await y;</c>
    ///     leaks <c>y</c> when <c>x</c> throws.
    /// </remarks>
    public static async Task<AgentProcess[]> TogetherAsync(params Task<AgentProcess>[] starting)
    {
        try
        {
            return await Task.WhenAll(starting);
        }
        catch
        {
            // WhenAll has let every start finish, so each one that succeeded is here to stop.
            foreach (var start in starting)
                if (start.IsCompletedSuccessfully)
                    await start.Result.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    ///     Starts a daemon on an ephemeral gossip port, joining <paramref name="peer" /> when given;
    ///     <paramref name="portArgument" /> is what <c>--port</c> receives.
    /// </summary>
    public static async Task<AgentProcess> StartAsync(string gossipKey, AgentProcess? peer = null, string portArgument = "0")
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
        start.ArgumentList.Add(portArgument);
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
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // WaitForExitAsync completes once the output has been read to its end too, which Exited does not
        // wait for: a message composed at Exited can miss everything the daemon printed before it exited.
        var exited = process.WaitForExitAsync();
        Task first;
        try
        {
            first = await Task.WhenAny(ready.Task, exited).WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await exited;
            throw new TimeoutException($"The Agent did not say it was ready within 30s:{Environment.NewLine}{Text(output)}");
        }

        if (first == exited && !ready.Task.IsCompleted)
            throw new InvalidOperationException(
                $"The Agent exited with code {process.ExitCode} before it was ready:{Environment.NewLine}{Text(output)}");

        var reported = Volatile.Read(ref gossipPort);
        if (reported == 0)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException(
                $"The Agent was ready without reporting the gossip port it bound:{Environment.NewLine}{Text(output)}");
        }

        return new AgentProcess(reported, socketPath, dataDirectory, process, exited, output);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        await _exited;

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
