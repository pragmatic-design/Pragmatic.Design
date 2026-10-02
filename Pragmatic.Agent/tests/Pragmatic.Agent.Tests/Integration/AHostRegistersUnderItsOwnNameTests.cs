using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Socket;
using Pragmatic.ControlPlane;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     A host registers with its Agent under the name its identity carries, not the entry
///     assembly's: every in-process host shares the test runner as entry assembly, and each was
///     <c>testhost</c> on the roster.
/// </summary>
/// <remarks>
///     Read from the Agent's roster, over the socket, as a second client — what the gateway would read.
/// </remarks>
public sealed class AHostRegistersUnderItsOwnNameTests : IAsyncLifetime
{
    private const string Reader = "roster-reader";

    private readonly string _pipeName = $"pragmatic-test-{Guid.NewGuid():N}";
    private AgentSocketServer? _server;

    public async Task InitializeAsync()
    {
        _server = new AgentSocketServer(_pipeName, new AgentMessageHandler(new KvStore(), "test-agent"));
        _server.Start();
        await Task.Delay(100);
    }

    public Task DisposeAsync()
    {
        _server?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task WithAnIdentity_TheHostIsListedUnderItsHostName()
    {
        var identity = new Identity("Orders.Host");
        var (hosts, keys) = await RosterWhileRunningAsync(new AgentOptions { SocketPath = _pipeName }, identity);

        // The name is the app; the id is the instance, one entry per running process.
        hosts.Select(host => (host.HostId, host.HostName)).Should().ContainSingle()
            .Which.Should().Be((identity.HostId, "Orders.Host"));
        keys.Should().Contain(HostRosterKeys.Key("Orders.Host", identity.HostId));
    }

    /// <summary>The control: an explicit <see cref="AgentOptions.AppId" /> still wins over the identity.</summary>
    [Fact]
    public async Task WithAnExplicitAppId_TheAppIdWins()
    {
        var options = new AgentOptions { SocketPath = _pipeName, AppId = "orders-a", AppName = "Orders A" };

        var identity = new Identity("Orders.Host");
        var (hosts, keys) = await RosterWhileRunningAsync(options, identity);

        hosts.Select(host => (host.HostId, host.HostName)).Should().ContainSingle()
            .Which.Should().Be((identity.HostId, "Orders A"));
        keys.Should().Contain(HostRosterKeys.Key("orders-a", identity.HostId), "the app id is the explicit one");
    }

    /// <summary>The roster without the reader, and the roster keys it was read from.</summary>
    private async Task<(IReadOnlyList<HostInfo> Hosts, IReadOnlyList<string> Keys)> RosterWhileRunningAsync(
        AgentOptions options, IHostIdentity identity)
    {
        await using var hostConnection = new AgentConnection(_pipeName);
        var service = new AgentHeartbeatService(hostConnection, options, identity: identity);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await using var readerConnection = new AgentConnection(_pipeName);
            await readerConnection.ConnectAsync();
            await readerConnection.RegisterAsync(Reader, Reader);
            using var controlPlane = new AgentControlPlane(readerConnection);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                var others = (await controlPlane.GetAllHostsAsync()).Where(host => host.HostName != Reader).ToList();
                if (others.Count > 0 || DateTime.UtcNow > deadline)
                {
                    var keys = (await readerConnection.KvPrefixAsync(HostRosterKeys.Prefix)).Select(entry => entry.Key).ToList();
                    return (others, keys);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private sealed record Identity(string HostName) : IHostIdentity
    {
        public string HostId { get; } = Guid.NewGuid().ToString("N");
        public HostType HostType => HostType.Tenant;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    }
}
