using Pragmatic.Agent.Client;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Gossip;

/// <summary>
///     An Agent started with <c>--port 0</c> binds a gossip port of its own and says which, so a peer can
///     join it there. Several Agents on one machine no longer need a port handed to them, which was free
///     when the caller chose it and could be taken before the Agent bound it.
/// </summary>
public sealed class AnAgentPicksItsOwnGossipPortTests
{
    private const string GossipKey = "an-agent-picks-its-own-gossip-port-test-key";

    [Fact]
    public async Task TwoAgentsStartedAtOnce_ReportDistinctPorts_AndAThirdJoinsTheFirstAtItsReportedOne()
    {
        var starting = new[] { AgentProcess.StartAsync(GossipKey), AgentProcess.StartAsync(GossipKey) };
        await using var first = await starting[0];
        await using var second = await starting[1];

        first.GossipPort.Should().BeGreaterThan(0);
        second.GossipPort.Should().BeGreaterThan(0);
        second.GossipPort.Should().NotBe(first.GossipPort, "each Agent bound a port of its own");

        await using var joiner = await AgentProcess.StartAsync(GossipKey, peer: first);
        await using var writer = await ConnectedAsync(joiner);
        await using var reader = await ConnectedAsync(first);

        // The effect of the join: what a client writes on the joiner reaches the Agent it joined.
        await writer.KvSetAsync("config/joined", "yes");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        var (value, _, found) = await reader.KvGetAsync("config/joined");
        while (!found && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            (value, _, found) = await reader.KvGetAsync("config/joined");
        }

        (found ? value : "<missing>").Should().Be("yes",
            $"the joiner reached the first Agent at the port it reported.{Environment.NewLine}" +
            $"First:{Environment.NewLine}{first.Output}{Environment.NewLine}Joiner:{Environment.NewLine}{joiner.Output}");
    }

    private static async Task<AgentConnection> ConnectedAsync(AgentProcess agent)
    {
        var connection = new AgentConnection(agent.SocketPath);
        await connection.ConnectAsync();
        (await connection.RegisterAsync("agent-port-test", "Agent port test")).Should().BeTrue();
        return connection;
    }
}
