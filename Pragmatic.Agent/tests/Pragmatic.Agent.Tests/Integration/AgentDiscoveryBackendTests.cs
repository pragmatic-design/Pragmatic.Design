using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.Discovery;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Socket;
using Pragmatic.Discovery.Models;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     P3.1: <see cref="AgentDiscoveryBackend"/> stores the FULL host topology (modules + boundaries) in
///     the Agent KV and reads it back — unlike the SignalR backend, which carried only liveness. Real
///     client ↔ daemon over a named pipe / Unix socket.
/// </summary>
public class AgentDiscoveryBackendTests : IAsyncLifetime
{
    private readonly string _pipeName = $"pragmatic-disc-{Guid.NewGuid():N}";
    private readonly KvStore _kvStore = new();
    private AgentSocketServer? _server;
    private AgentConnection? _client;

    public async Task InitializeAsync()
    {
        _server = new AgentSocketServer(_pipeName, new AgentMessageHandler(_kvStore, "test-agent"));
        _server.Start();
        await Task.Delay(100);

        _client = new AgentConnection(_pipeName);
        await _client.ConnectAsync();
        await _client.RegisterAsync("disc-host", "Discovery Host");
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
        _server?.Dispose();
    }

    private static HostTopologyInfo SampleTopology(string hostName) => new()
    {
        HostName = hostName,
        Modules =
        [
            new ModuleDeploymentInfo
            {
                ModuleName = "BillingModule",
                DatabaseName = "FinancialDb",
                Provider = "PostgreSql",
                ConfigKey = "ConnectionStrings:Financial",
                DbContext = "FinancialDbContext"
            }
        ],
        Boundaries =
        [
            new BoundaryReadAccessInfo { BoundaryName = "Booking", EntityTypes = ["Property", "RoomType"] }
        ]
    };

    [Fact]
    public async Task Store_Then_GetByHostName_RoundTripsFullTopology()
    {
        var backend = new AgentDiscoveryBackend(_client!);
        await backend.StoreAsync(SampleTopology("host-a"));

        var read = await backend.GetByHostNameAsync("host-a");

        read.Should().NotBeNull();
        read!.HostName.Should().Be("host-a");
        // The distinguishing property vs the SignalR backend: modules + boundaries survive the round-trip.
        read.Modules.Should().ContainSingle(m => m.ModuleName == "BillingModule"
            && m.DatabaseName == "FinancialDb" && m.ConfigKey == "ConnectionStrings:Financial");
        read.Boundaries.Should().ContainSingle(b => b.BoundaryName == "Booking"
            && b.EntityTypes.Contains("Property") && b.EntityTypes.Contains("RoomType"));
    }

    [Fact]
    public async Task GetAll_ReturnsEveryStoredHost()
    {
        var backend = new AgentDiscoveryBackend(_client!);
        await backend.StoreAsync(SampleTopology("host-1"));
        await backend.StoreAsync(SampleTopology("host-2"));

        var all = await backend.GetAllAsync();

        all.Should().HaveCount(2);
        all.Select(t => t.HostName).Should().BeEquivalentTo("host-1", "host-2");
    }

    [Fact]
    public async Task GetByHostName_Unknown_ReturnsNull()
    {
        var backend = new AgentDiscoveryBackend(_client!);

        (await backend.GetByHostNameAsync("never-stored")).Should().BeNull();
    }
}
