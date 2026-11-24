using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Gossip;

/// <summary>
///     What a test that starts Agent daemons leaves behind when one of them fails to start.
/// </summary>
/// <remarks>
///     A daemon left running holds the pipes it inherited from the test host, and the gate waits for
///     those to close, not for the test host to exit: one leaked daemon kept a full run waiting half an
///     hour after every test had finished.
/// </remarks>
public sealed class AgentProcessTests
{
    private const string GossipKey = "agent-process-tests-key-agent-process-tests-key";

    [Fact]
    public async Task TogetherAsync_OneDaemonExitsBeforeReady_StopsTheOneThatStartedAndSaysWhy()
    {
        var started = AgentProcess.StartAsync(GossipKey);
        var failing = AgentProcess.StartAsync(GossipKey, portArgument: "not-a-port");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AgentProcess.TogetherAsync(started, failing));

        failure.Message.Should().Contain("Invalid value for --port");
        (await started).HasExited.Should().BeTrue("a daemon left running keeps the gate waiting on its pipes");
    }
}
