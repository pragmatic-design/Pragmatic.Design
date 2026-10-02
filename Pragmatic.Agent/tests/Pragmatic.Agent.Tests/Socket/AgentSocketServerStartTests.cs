using System.IO.Pipes;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Socket;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Socket;

/// <summary>
///     <see cref="AgentSocketServer.Start" /> reports a server that could not start listening, instead
///     of returning as if it were.
/// </summary>
/// <remarks>
///     <para>
///         Binding ran inside the listen loop, before its first await. On Unix the exception went into a
///         task nobody observed: a bare socket name made Directory.CreateDirectory("") throw there, and
///         twenty-five integration tests then failed on the client, one step away from the cause.
///         On Windows a pipe that could not be created was caught and retried with no await
///         in between, so Start() never returned at all — measured by the first version of this test,
///         which hung.
///     </para>
///     <para>
///         Hence the deadline: a regression must fail this test, not hang the suite.
///     </para>
/// </remarks>
public sealed class AgentSocketServerStartTests
{
    [Fact]
    public async Task Start_WhenTheServerCannotListen_Throws()
    {
        // Windows: a pipe name already held with a single instance. Unix: a socket whose directory is a
        // regular file.
        var name = $"pragmatic-test-{Guid.NewGuid():N}";
        var file = OperatingSystem.IsWindows() ? null : Path.GetTempFileName();
        using var holder = OperatingSystem.IsWindows()
            ? new NamedPipeServerStream(name, PipeDirection.InOut, maxNumberOfServerInstances: 1)
            : null;
        using var server = new AgentSocketServer(
            file is null ? name : Path.Combine(file, "agent.sock"),
            new AgentMessageHandler(new KvStore(), "test-agent"));

        try
        {
            var start = Task.Run(server.Start);
            var finished = await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(true);
            finished.Should().Be(start, "Start() must return, not retry forever an endpoint it cannot open");

            var act = () => start;
            await act.Should().ThrowAsync<IOException>().ConfigureAwait(true);
        }
        finally
        {
            if (file is not null)
                File.Delete(file);
        }
    }
}
