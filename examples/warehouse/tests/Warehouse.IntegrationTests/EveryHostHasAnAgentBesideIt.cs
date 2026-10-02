using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Agent.Client;
using Pragmatic.Gateway;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;

namespace Warehouse.IntegrationTests;

/// <summary>
///     Every host, and the gateway, is connected to the Agent that runs beside it.
/// </summary>
/// <remarks>
///     <para>
///         Read from the host's side: its Agent connection is open, on the socket of the Agent the suite
///         started beside it. The Agents form one cluster, so a roster read from any of them
///         lists every host of the application — which is what a roster is for, and why it cannot say
///         which Agent a host is connected to.
///     </para>
///     <para>
///         A host connects from a background service after it starts, so the connection is waited for,
///         bounded, rather than read once.
///     </para>
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class EveryHostHasAnAgentBesideIt(WarehouseFixture warehouse)
{
    [Theory]
    [InlineData("orders")]
    [InlineData("stock-a")]
    [InlineData("stock-b")]
    [InlineData("shipping")]
    [InlineData("gateway")]
    public async Task TheHost_IsConnected_ToTheAgentBesideIt(string agent)
    {
        var (services, socketPath) = HostOf(agent);
        var connection = services.GetRequiredService<AgentConnection>();

        await WarehouseWaits.UntilAsync(() => Task.FromResult(connection.IsConnected), connected => connected,
            $"the {agent} host connected to its Agent. The Agent printed:{Environment.NewLine}{warehouse.Agents[agent].Output}");

        socketPath.Should().Be(warehouse.Agents[agent].SocketPath, "the Agent beside it, and no other");
    }

    /// <summary>
    ///     Each host is on the roster under its own name, the one its identity carries, not the
    ///     entry assembly's: in this suite the entry assembly is the test runner, and every host was
    ///     <c>testhost</c>.
    /// </summary>
    /// <remarks>
    ///     Read from the Agent beside the host. The name is what a roster reader — the gateway routing to
    ///     the instances that announce themselves — knows a host by.
    /// </remarks>
    [Theory]
    [InlineData("orders")]
    [InlineData("stock-a")]
    [InlineData("stock-b")]
    [InlineData("shipping")]
    public async Task TheAgent_ListsItsHost_UnderTheHostsOwnName(string agent)
    {
        var expected = HostOf(agent).Services.GetRequiredService<Pragmatic.ControlPlane.IHostIdentity>().HostName;

        var names = await WarehouseWaits.UntilAsync(
            async () => (await RosterOfAsync(warehouse.Agents[agent].SocketPath)).Select(host => host.HostName).ToList(),
            listed => listed.Contains(expected),
            $"the roster of the {agent} Agent lists {expected}");

        names.Should().Contain(expected);
        names.Should().NotContain("testhost", "no host registers under the test runner's name");
    }

    /// <summary>
    ///     Five processes are five roster entries, read from any Agent of the cluster: the two Stock
    ///     instances are two entries, each under its own instance id.
    /// </summary>
    /// <remarks>
    ///     The roster had one key per app id: Stock A and B shared <c>state/app:Warehouse.Stock.Host</c>, the
    ///     roster listed four entries, and stopping either took both off it.
    /// </remarks>
    [Theory]
    [InlineData("gateway")]
    [InlineData("stock-b")]
    public async Task TheRoster_ListsEveryProcess_StockTwice(string agent)
    {
        string[] expected =
        [
            "Warehouse.Orders.Host", "Warehouse.Stock.Host", "Warehouse.Stock.Host", "Warehouse.Shipping.Host",
            "Pragmatic Gateway",
        ];

        var roster = await WarehouseWaits.UntilAsync(
            () => RosterOfAsync(warehouse.Agents[agent].SocketPath),
            hosts => hosts.Count >= expected.Length,
            $"the roster of the {agent} Agent lists the five processes");

        roster.Select(host => host.HostName).Should().BeEquivalentTo(expected);
        roster.Where(host => host.HostName == "Warehouse.Stock.Host").Select(host => host.HostId).Should().BeEquivalentTo(
            [IdOf(warehouse.StockA.Services), IdOf(warehouse.StockB.Services)]);
    }

    private static string IdOf(IServiceProvider services)
        => services.GetRequiredService<Pragmatic.ControlPlane.IHostIdentity>().HostId;

    private (IServiceProvider Services, string SocketPath) HostOf(string agent) => agent switch
    {
        "orders" => (warehouse.Orders.Services, SocketOf(warehouse.Orders.Services)),
        "stock-a" => (warehouse.StockA.Services, SocketOf(warehouse.StockA.Services)),
        "stock-b" => (warehouse.StockB.Services, SocketOf(warehouse.StockB.Services)),
        "shipping" => (warehouse.Shipping.Services, SocketOf(warehouse.Shipping.Services)),
        "gateway" => (warehouse.Gateway.Services,
            warehouse.Gateway.Services.GetRequiredService<GatewayOptions>().AgentSocketPath),
        _ => throw new ArgumentOutOfRangeException(nameof(agent), agent, "no host stands beside this Agent"),
    };

    private static string SocketOf(IServiceProvider services) => services.GetRequiredService<AgentOptions>().SocketPath;

    /// <summary>The app id the suite registers under to read a roster, and leaves out of it.</summary>
    private const string Reader = "warehouse-suite-roster-reader";

    /// <summary>The Agent's roster, without the suite's own connections.</summary>
    /// <remarks>
    ///     The reader registers first: the daemon refuses every request from a client that has not.
    ///     The suite's other connections — its operator, its diagnostics, its rotation check — register too, for
    ///     as long as they are open, and are not processes of the application.
    /// </remarks>
    private static async Task<IReadOnlyList<Pragmatic.ControlPlane.HostInfo>> RosterOfAsync(string socket)
    {
        await using var connection = new AgentConnection(socket);
        await connection.ConnectAsync();
        await connection.RegisterAsync(Reader, Reader);
        using var controlPlane = new AgentControlPlane(connection);

        return (await controlPlane.GetAllHostsAsync())
            .Where(host => host.HostName != Reader && !host.HostName.StartsWith("warehouse-suite-", StringComparison.Ordinal))
            .ToList();
    }
}
