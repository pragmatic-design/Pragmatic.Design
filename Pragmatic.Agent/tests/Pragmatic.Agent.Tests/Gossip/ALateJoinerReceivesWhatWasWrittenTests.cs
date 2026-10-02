using System.Net;
using System.Net.Sockets;
using Pragmatic.Agent.Gossip;
using Pragmatic.Agent.KV;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Gossip;

/// <summary>
///     An Agent that joins after a key was written receives it: the piggyback that carried the
///     write is long spent by then, and only the state exchange can bring it.
/// </summary>
public sealed class ALateJoinerReceivesWhatWasWrittenTests
{
    private const string SharedKey = "a-late-joiner-receives-test-key";

    [Fact]
    public async Task AKeyWrittenBeforeTheJoin_ReachesTheJoiner()
    {
        var (early, middle, late) = (new KvStore(), new KvStore(), new KvStore());
        var earlyPort = FreeUdpPort();
        using var first = new ClusterManager(Config("early", earlyPort, peers: []), early);
        await first.StartAsync();
        using var second = new ClusterManager(Config("middle", FreeUdpPort(), peers: [$"127.0.0.1:{earlyPort}"]), middle);
        await second.StartAsync();

        early.Set("gateway/instances/orders/a", "http://a");
        await UntilAsync(() => middle.Get("gateway/instances/orders/a") is not null);
        // The write's piggyback budget is spent on the two Agents that exist: the one that joins next
        // can only have it from a state exchange.
        await Task.Delay(TimeSpan.FromSeconds(5));

        using var third = new ClusterManager(Config("late", FreeUdpPort(), peers: [$"127.0.0.1:{earlyPort}"]), late);
        await third.StartAsync();
        await UntilAsync(() => late.Get("gateway/instances/orders/a") is not null);

        (late.Get("gateway/instances/orders/a")?.Value ?? "<missing>").Should().Be("http://a");
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
    }

    private static ClusterConfig Config(string id, int port, List<string> peers) => new()
    {
        AgentId = $"{id}-{Guid.NewGuid():N}",
        GossipPort = port,
        BindAddress = "127.0.0.1",
        SharedKey = SharedKey,
        Discovery = "static",
        Peers = peers,
    };

    private static int FreeUdpPort()
    {
        using var socket = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
