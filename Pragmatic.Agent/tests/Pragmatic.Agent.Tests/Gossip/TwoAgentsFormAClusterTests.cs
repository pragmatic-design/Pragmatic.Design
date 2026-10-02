using System.Net;
using System.Net.Sockets;
using Pragmatic.Agent.Gossip;
using Pragmatic.Agent.KV;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Gossip;

/// <summary>
///     An Agent that joins a peer forms a cluster with it, and what one's clients write reaches
///     the other: the only reason there is more than one Agent.
/// </summary>
/// <remarks>
///     Two real <see cref="ClusterManager" />s on loopback, over UDP. The member table never held the
///     Agent itself, so a Join named nobody, the peer learned nothing, and no update was ever sent.
/// </remarks>
public sealed class TwoAgentsFormAClusterTests
{
    private const string SharedKey = "two-agents-form-a-cluster-test-key";

    [Fact]
    public async Task AKeySetOnTheJoiner_ReachesThePeer_AndSoDoesItsDelete()
    {
        var (first, second) = (new KvStore(), new KvStore());
        using var joined = await ClusterAsync(first, second, SharedKey);

        second.Set("gateway/instances/orders/b", "http://b");
        await UntilAsync(() => first.Get("gateway/instances/orders/b") is not null);
        ValueOf(first, "gateway/instances/orders/b").Should().Be("http://b", "the peer holds what the joiner's client wrote");

        second.Delete("gateway/instances/orders/b");
        await UntilAsync(() => first.Get("gateway/instances/orders/b") is null);
        first.Get("gateway/instances/orders/b").Should().BeNull("the delete travels like the write");
    }

    [Fact]
    public async Task AKeySetOnThePeer_ReachesTheJoiner()
    {
        var (first, second) = (new KvStore(), new KvStore());
        using var joined = await ClusterAsync(first, second, SharedKey);

        first.Set("config/limit", "10");
        await UntilAsync(() => second.Get("config/limit") is not null);

        ValueOf(second, "config/limit").Should().Be("10");
    }

    /// <summary>The value held under <paramref name="key" />, or a text that says it is missing — never a null that skips the assertion.</summary>
    private static string ValueOf(KvStore store, string key) => store.Get(key)?.Value ?? "<missing>";

    /// <summary>The control: without a shared key an Agent applies nothing it hears (fail-closed).</summary>
    [Fact]
    public async Task WithoutASharedKey_NothingIsReplicated()
    {
        var (first, second) = (new KvStore(), new KvStore());
        using var joined = await ClusterAsync(first, second, sharedKey: null);

        second.Set("gateway/instances/orders/b", "http://b");
        await UntilAsync(() => first.Get("gateway/instances/orders/b") is not null, TimeSpan.FromSeconds(4));

        first.Get("gateway/instances/orders/b").Should().BeNull();
    }

    /// <summary>The first Agent alone, the second joining it; disposed together.</summary>
    private static async Task<IDisposable> ClusterAsync(KvStore first, KvStore second, string? sharedKey)
    {
        var firstPort = FreeUdpPort();
        var peer = new ClusterManager(Config("first", firstPort, sharedKey, peers: []), first);
        await peer.StartAsync();
        var joiner = new ClusterManager(Config("second", FreeUdpPort(), sharedKey, peers: [$"127.0.0.1:{firstPort}"]), second);
        await joiner.StartAsync();
        return new Both(peer, joiner);
    }

    private static ClusterConfig Config(string id, int port, string? sharedKey, List<string> peers) => new()
    {
        AgentId = $"{id}-{Guid.NewGuid():N}",
        GossipPort = port,
        BindAddress = "127.0.0.1",
        SharedKey = sharedKey,
        Discovery = "static",
        Peers = peers,
    };

    private static async Task UntilAsync(Func<bool> condition, TimeSpan? within = null)
    {
        var deadline = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(10));
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
    }

    private static int FreeUdpPort()
    {
        using var socket = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private sealed class Both(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            second.Dispose();
            first.Dispose();
        }
    }
}
