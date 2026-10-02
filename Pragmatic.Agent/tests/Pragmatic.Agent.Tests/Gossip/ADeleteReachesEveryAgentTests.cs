using System.Net;
using System.Net.Sockets;
using Pragmatic.Agent.Gossip;
using Pragmatic.Agent.KV;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Gossip;

/// <summary>
///     In a cluster of five, joined as the Warehouse suite joins them (each to one hub), a key
///     written on one Agent and then deleted is gone from every Agent: what an instance announced leaves
///     with it everywhere, round after round.
/// </summary>
public sealed class ADeleteReachesEveryAgentTests
{
    private const string SharedKey = "a-delete-reaches-every-agent-test-key";

    [Fact]
    public async Task TenRoundsOfWriteThenDelete_EveryAgentEndsWithout_TheKey()
    {
        var stores = Enumerable.Range(0, 5).Select(_ => new KvStore()).ToArray();
        var managers = await ClusterAsync(stores);
        try
        {
            for (var round = 0; round < 10; round++)
            {
                var key = $"gateway/instances/warehouse/round-{round}";
                var writer = stores[1 + round % 4];

                writer.Set(key, "http://b");
                await UntilAsync(() => stores.All(s => s.Get(key) is not null));
                stores.Count(s => s.Get(key) is not null).Should().Be(5, $"round {round}: the write reached every Agent");

                writer.Delete(key);
                await UntilAsync(() => stores.All(s => s.Get(key) is null));
                stores.Count(s => s.Get(key) is not null).Should().Be(0, $"round {round}: the delete reached every Agent");
            }
        }
        finally
        {
            foreach (var manager in managers)
                manager.Dispose();
        }
    }

    private static async Task<List<ClusterManager>> ClusterAsync(KvStore[] stores)
    {
        var hubPort = FreeUdpPort();
        var managers = new List<ClusterManager>();
        for (var i = 0; i < stores.Length; i++)
        {
            var config = new ClusterConfig
            {
                AgentId = $"agent-{i}-{Guid.NewGuid():N}",
                GossipPort = i == 0 ? hubPort : FreeUdpPort(),
                BindAddress = "127.0.0.1",
                SharedKey = SharedKey,
                Discovery = "static",
                Peers = i == 0 ? [] : [$"127.0.0.1:{hubPort}"],
            };
            var manager = new ClusterManager(config, stores[i]);
            await manager.StartAsync();
            managers.Add(manager);
        }

        return managers;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
    }

    private static int FreeUdpPort()
    {
        using var socket = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
