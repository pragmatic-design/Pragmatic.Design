using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Socket;
using Pragmatic.ControlPlane;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     Two instances of one host are two roster entries, and one leaving takes only its own.
/// </summary>
/// <remarks>
///     The roster had one key per app id, <c>state/app:{appId}</c>: the second instance overwrote the first,
///     and the first to disconnect deleted the entry the other was still using. Measured on Warehouse, five
///     processes were four entries, and stopping one Stock instance took the other off the roster.
/// </remarks>
public sealed class EachInstanceIsOnTheRosterTests : IAsyncLifetime
{
    private const string Reader = "roster-reader";
    private const string Stock = "Warehouse.Stock.Host";

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
    public async Task TwoInstancesOfOneHost_AreTwoEntries_EachUnderItsInstanceId()
    {
        var (a, b) = (new Identity(Stock), new Identity(Stock));
        await using var instanceA = await StartedAsync(a);
        await using var instanceB = await StartedAsync(b);

        var roster = await RosterUntilAsync(hosts => hosts.Count >= 2);

        roster.Select(host => (host.HostId, host.HostName)).Should().BeEquivalentTo(
            [(a.HostId, Stock), (b.HostId, Stock)]);
    }

    [Fact]
    public async Task OneInstanceStopping_LeavesTheOtherListed()
    {
        var (a, b) = (new Identity(Stock), new Identity(Stock));
        var instanceA = await StartedAsync(a);
        await using var instanceB = await StartedAsync(b);
        await RosterUntilAsync(hosts => hosts.Count >= 2);

        await instanceA.DisposeAsync();

        var roster = await RosterUntilAsync(hosts => hosts.All(host => host.HostId != a.HostId));
        roster.Select(host => host.HostId).Should().Equal([b.HostId], "B is still running");
    }

    private async Task<RunningInstance> StartedAsync(IHostIdentity identity)
    {
        var connection = new AgentConnection(_pipeName);
        var service = new AgentHeartbeatService(connection, new AgentOptions { SocketPath = _pipeName }, identity: identity);
        await service.StartAsync(CancellationToken.None);
        return new RunningInstance(connection, service);
    }

    /// <summary>The roster without the reader, once <paramref name="until" /> holds or ten seconds pass.</summary>
    private async Task<IReadOnlyList<HostInfo>> RosterUntilAsync(Func<IReadOnlyList<HostInfo>, bool> until)
    {
        await using var connection = new AgentConnection(_pipeName);
        await connection.ConnectAsync();
        await connection.RegisterAsync(Reader, Reader);
        using var controlPlane = new AgentControlPlane(connection);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var hosts = (await controlPlane.GetAllHostsAsync()).Where(host => host.HostName != Reader).ToList();
            if (until(hosts) || DateTime.UtcNow > deadline)
                return hosts;

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private sealed class RunningInstance(AgentConnection connection, AgentHeartbeatService service) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await service.StopAsync(CancellationToken.None);
            await connection.DisposeAsync();
        }
    }

    private sealed record Identity(string HostName) : IHostIdentity
    {
        public string HostId { get; } = Guid.NewGuid().ToString("N");
        public HostType HostType => HostType.Tenant;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    }
}
