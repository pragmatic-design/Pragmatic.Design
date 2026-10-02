using System.Text.Json;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Socket;
using Pragmatic.ControlPlane;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     A host configured with a route announces it, and its address, to its Agent when it
///     registers; the announcement goes when the host does.
/// </summary>
public sealed class AHostAnnouncesTheRouteItServesTests : IAsyncLifetime
{
    private readonly string _pipeName = $"pragmatic-test-{Guid.NewGuid():N}";
    private readonly KvStore _store = new();
    private AgentSocketServer? _server;

    public async Task InitializeAsync()
    {
        _server = new AgentSocketServer(_pipeName, new AgentMessageHandler(_store, "test-agent"));
        _server.Start();
        await Task.Delay(100);
    }

    public Task DisposeAsync()
    {
        _server?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task WithAnAnnouncement_TheInstanceIsOnTheKv_UnderItsRouteAndItsOwnId()
    {
        var identity = new Identity("Stock.Host");
        var options = new AgentOptions
        {
            SocketPath = _pipeName,
            Announce = new AgentRouteAnnouncement
            {
                RouteId = "warehouse",
                Path = "/warehouse/{**catch-all}",
                PathRemovePrefix = "/warehouse",
                RequireAuth = true,
                Address = "http://127.0.0.1:5222",
            },
        };
        var key = InstanceAnnouncementKeys.Key("warehouse", identity.HostId);

        await using (var connection = new AgentConnection(_pipeName))
        {
            var service = new AgentHeartbeatService(connection, options, identity: identity);
            await service.StartAsync(CancellationToken.None);
            await UntilAsync(() => _store.Get(key) is not null);
            await service.StopAsync(CancellationToken.None);

            var announced = JsonSerializer.Deserialize<InstanceAnnouncementPayload>(_store.Get(key)!.Value!)!;
            (announced.Address, announced.Path, announced.PathRemovePrefix, announced.RequireAuth, announced.AppId)
                .Should().Be(("http://127.0.0.1:5222", "/warehouse/{**catch-all}", "/warehouse", true, "Stock.Host"));
        }

        await UntilAsync(() => _store.Get(key) is null);
        _store.Get(key).Should().BeNull("the instance stopped, so it leaves the rotation");
    }

    /// <summary>The control: a host that announces nothing writes nothing under the prefix.</summary>
    [Fact]
    public async Task WithoutAnAnnouncement_NothingIsAnnounced()
    {
        var identity = new Identity("Orders.Host");
        await using var connection = new AgentConnection(_pipeName);
        var service = new AgentHeartbeatService(connection, new AgentOptions { SocketPath = _pipeName }, identity: identity);

        await service.StartAsync(CancellationToken.None);
        var rosterKey = HostRosterKeys.Key("Orders.Host", identity.HostId);
        await UntilAsync(() => _store.Get(rosterKey) is not null);
        await service.StopAsync(CancellationToken.None);

        _store.Get(rosterKey).Should().NotBeNull("the host did register");
        _store.GetByPrefix(InstanceAnnouncementKeys.Prefix).Should().BeEmpty();
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
    }

    private sealed record Identity(string HostName) : IHostIdentity
    {
        public string HostId { get; } = Guid.NewGuid().ToString("N");
        public HostType HostType => HostType.Tenant;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    }
}
