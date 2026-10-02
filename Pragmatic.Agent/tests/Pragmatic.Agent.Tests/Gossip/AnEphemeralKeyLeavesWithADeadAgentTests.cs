using System.Net;
using System.Net.Sockets;
using Pragmatic.Agent.Gossip;
using Pragmatic.Agent.KV;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Gossip;

/// <summary>
///     An ephemeral key goes with its client's Agent, not only with its client: when the Agent
///     that holds the client dies, the others delete what that client announced.
/// </summary>
/// <remarks>
///     <para>
///         C is stopped by disposing its cluster manager: its gossip socket closes and nothing is said to
///         the others, which is what a crash or a lost machine looks like from the outside. Its store is
///         left untouched — a crashed process deletes nothing.
///     </para>
///     <para>
///         The plain key written by the same Agent is the control: "the key is gone" holds just as well for
///         a store that dropped everything C ever wrote, or everything at all.
///     </para>
/// </remarks>
public sealed class AnEphemeralKeyLeavesWithADeadAgentTests
{
    private const string SharedKey = "an-ephemeral-key-leaves-with-a-dead-agent-test-key";
    private static readonly TimeSpan SuspectTimeout = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task WhenTheAgentHoldingTheClientDies_TheOthersDeleteItsEphemeralKey_AndKeepItsPlainOne()
    {
        var stores = Enumerable.Range(0, 3).Select(_ => new KvStore()).ToArray();
        var (managers, ids) = await ClusterAsync(stores);
        try
        {
            const string announced = "gateway/instances/warehouse/c-instance";
            const string plain = "config/warehouse/written-through-c";
            stores[2].Set(announced, "http://c", owner: ids[2]);
            stores[2].Set(plain, "kept");

            await UntilAsync(() => stores.All(s => s.Get(announced) is not null && s.Get(plain) is not null));
            stores[0].Get(announced)!.Owner.Should().Be(ids[2], "the owner travels with the gossiped write");
            stores[1].Get(announced)!.Owner.Should().Be(ids[2]);

            // Removed from the list first: SwimProtocol.Dispose is not idempotent and throws the second time.
            var c = managers[2];
            managers.RemoveAt(2);
            c.Dispose();

            await UntilAsync(() => stores[0].Get(announced) is null && stores[1].Get(announced) is null);

            stores[0].Get(announced).Should().BeNull("A learnt that C died, and C owned the key");
            stores[1].Get(announced).Should().BeNull("so did B");
            stores[0].Get(plain)!.Value.Should().Be("kept", "the control: a plain key outlives the Agent that wrote it");
            stores[1].Get(plain)!.Value.Should().Be("kept");
        }
        finally
        {
            foreach (var manager in managers)
                manager.Dispose();
        }
    }

    private static async Task<(List<ClusterManager> Managers, string[] Ids)> ClusterAsync(KvStore[] stores)
    {
        var hubPort = FreeUdpPort();
        var managers = new List<ClusterManager>();
        var ids = new string[stores.Length];
        for (var i = 0; i < stores.Length; i++)
        {
            ids[i] = $"agent-{i}-{Guid.NewGuid():N}";
            var config = new ClusterConfig
            {
                AgentId = ids[i],
                GossipPort = i == 0 ? hubPort : FreeUdpPort(),
                BindAddress = "127.0.0.1",
                SharedKey = SharedKey,
                Discovery = "static",
                Peers = i == 0 ? [] : [$"127.0.0.1:{hubPort}"],
                SuspectTimeout = SuspectTimeout,
            };
            var manager = new ClusterManager(config, stores[i]);
            await manager.StartAsync();
            managers.Add(manager);
        }

        return (managers, ids);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
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
