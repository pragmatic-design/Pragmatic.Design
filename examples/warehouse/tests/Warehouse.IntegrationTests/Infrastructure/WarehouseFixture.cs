using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;
using Warehouse.Orders.Host;
using Warehouse.Shipping.Host;
using Warehouse.Stock.Host;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     The whole example, started once: PostgreSQL, RabbitMQ, an Agent beside every host, Orders, Stock
///     twice, Shipping, and the gateway in front of them.
/// </summary>
/// <remarks>
///     <para>
///         Each host gets an Agent of its own because in a deployment each runs on its own machine, where
///         one Agent serves the processes of that machine. Here they share one, so the suite gives every
///         Agent its own socket and gossip port — the only difference from a deployment it introduces.
///     </para>
///     <para>
///         The gateway publishes each service under a prefix and removes it before forwarding, so the
///         services answer on their own paths; the Stock route has both instances. The routes are not
///         configured on the gateway: each host announces its own to its Agent, the Agents gossip them as
///         one cluster, and an instance that stops leaves the rotation with its connection.
///     </para>
/// </remarks>
public sealed class WarehouseFixture : IAsyncLifetime
{
    /// <summary>The key the hosts validate tokens with. Long enough for HMAC-SHA256.</summary>
    internal const string JwtKey = "warehouse-integration-suite-signing-key-not-a-secret";

    /// <summary>How long Orders waits for Stock. Short, so a test of the silence does not wait long.</summary>
    internal static readonly TimeSpan RequestReplyTimeout = TimeSpan.FromSeconds(3);

    private readonly PostgresFixture _postgres = new();
    private readonly RabbitMqFixture _rabbit = new();

    /// <summary>The channel the two Stock instances broadcast cache invalidations over.</summary>
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
    private readonly List<AgentDaemon> _agents = [];

    internal ServiceHost<OrdersHost> Orders { get; private set; } = null!;

    /// <summary>The broker, for a test that has to read what crossed it.</summary>
    internal RabbitMqFixture Broker => _rabbit;

    internal ServiceHost<StockHost> StockA { get; private set; } = null!;

    internal ServiceHost<StockHost> StockB { get; private set; } = null!;

    internal ServiceHost<ShippingHost> Shipping { get; private set; } = null!;

    internal GatewayHost Gateway { get; private set; } = null!;

    /// <summary>The Agent beside each host, by the name the suite gives the host.</summary>
    internal IReadOnlyDictionary<string, AgentDaemon> Agents { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // The gateway's Agent first: the others join the cluster through it, and it is the one that must
        // hear every announcement.
        var containers = Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync(), _redis.StartAsync());
        var gatewayAgent = await AgentDaemon.StartAsync("gateway", GossipKey).ConfigureAwait(false);
        var others = new[] { "orders", "stock-a", "stock-b", "shipping" }
            .Select(name => AgentDaemon.StartAsync(name, GossipKey, peer: gatewayAgent))
            .ToArray();
        await Task.WhenAll(containers, Task.WhenAll(others)).ConfigureAwait(false);

        _agents.Add(gatewayAgent);
        _agents.AddRange(others.Select(agent => agent.Result));
        Agents = _agents.ToDictionary(agent => agent.Name);

        Orders = StartAnnounced<OrdersHost>(OrdersSettings(_rabbit.ConnectionString), "orders");
        StockA = StartAnnounced<StockHost>(StockSettings(Agents["stock-a"]), "warehouse");
        StockB = StartAnnounced<StockHost>(StockSettings(Agents["stock-b"]), "warehouse");
        var shipping = HostSettings("Shipping", _postgres.ShippingConnectionString, Agents["shipping"]);
        shipping["Messaging:RabbitMq:ConnectionString"] = _rabbit.ConnectionString;
        Shipping = StartAnnounced<ShippingHost>(shipping, "shipping");

        // No route here: each host announced its own to the Agent beside it, and the gateway's Agent tells
        // the gateway.
        Gateway = new GatewayHost(new Dictionary<string, string?>
        {
            ["Gateway:AgentSocketPath"] = Agents["gateway"].SocketPath,

            // The edge checks who: a token signed with the key the services share, for this issuer and
            // audience. Each service still checks what the token may do.
            ["Gateway:Jwt:SigningKey"] = JwtKey,
            ["Gateway:Jwt:Issuer"] = "warehouse",
            ["Gateway:Jwt:Audience"] = "warehouse",

            // Stock's own resilience at the edge: it is the cluster every order asks, twice over.
            ["Gateway:Resilience:Clusters:warehouse:Timeout"] = "00:00:05",
            ["Gateway:Resilience:Clusters:warehouse:CircuitBreakerEnabled"] = "true",
            ["Gateway:Resilience:Clusters:warehouse:FailureThreshold"] = "5",
            ["Gateway:Resilience:Clusters:warehouse:BreakDuration"] = "00:00:10",
        });

        // Started here and not by the first test that asks for a client: a factory builds its host lazily,
        // and a gateway nobody has called yet has not connected to its Agent either.
        Gateway.StartServer();

        await UntilEveryInstanceIsRoutedAsync().ConfigureAwait(false);
    }

    /// <summary>The key the Agents authenticate gossip with — without one, none applies another's writes.</summary>
    private const string GossipKey = "warehouse-integration-suite-gossip-key";

    /// <summary>
    ///     Starts a host on a port chosen first, announcing <paramref name="routeId" /> at that address: the
    ///     address has to be in its configuration before it starts, as it is in a deployment.
    /// </summary>
    private static ServiceHost<THost> StartAnnounced<THost>(Dictionary<string, string?> settings, string routeId)
        where THost : class
    {
        var port = FreeTcpPort();
        settings["Pragmatic:Agent:Announce:RouteId"] = routeId;
        settings["Pragmatic:Agent:Announce:Path"] = $"/{routeId}/{{**catch-all}}";
        settings["Pragmatic:Agent:Announce:PathRemovePrefix"] = $"/{routeId}";
        settings["Pragmatic:Agent:Announce:RequireAuth"] = "true";
        settings["Pragmatic:Agent:Announce:Address"] = $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}";
        return new ServiceHost<THost>(settings).Start(port);
    }

    /// <summary>
    ///     Returns once the gateway routes to every instance: each prefix answers something other than its
    ///     404, and the Stock route has been heard from both instances.
    /// </summary>
    /// <remarks>
    ///     An announcement travels host → Agent → gossip → the gateway's Agent → the gateway, a moment
    ///     after the hosts start; a test that ran first would meet a gateway that does not know them yet.
    /// </remarks>
    private async Task UntilEveryInstanceIsRoutedAsync()
    {
        await UntilInRotationAsync("orders", Orders.Services).ConfigureAwait(false);
        await UntilInRotationAsync("warehouse", StockA.Services).ConfigureAwait(false);
        await UntilInRotationAsync("warehouse", StockB.Services).ConfigureAwait(false);
        await UntilInRotationAsync("shipping", Shipping.Services).ConfigureAwait(false);
        await UntilBothStockInstancesServeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Returns once the gateway's Agent holds this instance's announcement and the gateway routes its prefix.
    /// </summary>
    /// <remarks>
    ///     The instance's own key and not a response: a restarted instance listens where the previous one
    ///     did, so until the old announcement is deleted a request already reaches the new process — and
    ///     when that delete lands, the route would go with it unless the new announcement is already there.
    /// </remarks>
    private async Task UntilInRotationAsync(string routeId, IServiceProvider instance)
    {
        var key = Pragmatic.Agent.Protocol.Payloads.InstanceAnnouncementKeys.Key(
            routeId, instance.GetRequiredService<Pragmatic.ControlPlane.IHostIdentity>().HostId);
        using var anonymous = Gateway.CreateClient();
        try
        {
            await using var gatewayAgent = new Pragmatic.Agent.Client.AgentConnection(Agents["gateway"].SocketPath);
            await gatewayAgent.ConnectAsync().ConfigureAwait(false);
            await gatewayAgent.RegisterAsync("warehouse-suite-rotation", "warehouse-suite-rotation").ConfigureAwait(false);
            await WarehouseWaits.UntilAsync(
                async () => (await gatewayAgent.KvGetAsync(key).ConfigureAwait(false)).Found,
                found => found,
                $"the gateway's Agent holds {key}").ConfigureAwait(false);

            await WarehouseWaits.UntilAsync(
                async () => (await anonymous.GetAsync($"/{routeId}/health").ConfigureAwait(false)).StatusCode,
                status => status != System.Net.HttpStatusCode.NotFound,
                $"the gateway routes /{routeId}").ConfigureAwait(false);
        }
        catch (TimeoutException timeout)
        {
            // Which link broke — a host that did not announce, gossip that did not carry it, or a
            // gateway that did not reload — is read off what each Agent holds.
            throw new TimeoutException(
                $"{timeout.Message}{Environment.NewLine}{await AnnouncementsByAgentAsync().ConfigureAwait(false)}", timeout);
        }
    }

    /// <summary>Returns once both Stock instances have answered through the gateway.</summary>
    private async Task UntilBothStockInstancesServeAsync()
    {
        using var stock = StockCalls.ThroughTheGateway(this, StockCalls.Manager);
        await WarehouseWaits.UntilAsync(
            async () =>
            {
                var servedBy = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < 4; i++)
                {
                    using var response = await stock.GetAsync("health").ConfigureAwait(false);
                    if (response.Headers.TryGetValues("X-Served-By", out var values))
                        servedBy.Add(values.Single());
                }

                return servedBy;
            },
            servedBy => servedBy.SetEquals([StockA.ServedBy, StockB.ServedBy]),
            "both Stock instances in the gateway's rotation").ConfigureAwait(false);
    }

    /// <summary>
    ///     Writes a key through the gateway's Agent, as an operator's tool would: the cluster carries it to
    ///     the Agent beside every host.
    /// </summary>
    internal async Task WriteThroughTheAgentAsync(string key, string value)
    {
        await using var agent = await OperatorConnectionAsync().ConfigureAwait(false);
        await agent.KvSetAsync(key, value).ConfigureAwait(false);
    }

    /// <summary>Deletes a key through the gateway's Agent: the setting falls back to what the hosts configure.</summary>
    internal async Task DeleteThroughTheAgentAsync(string key)
    {
        await using var agent = await OperatorConnectionAsync().ConfigureAwait(false);
        await agent.KvDeleteAsync(key).ConfigureAwait(false);
    }

    /// <summary>
    ///     Sends a command to one instance through the gateway's Agent, as an operator's tool would: the
    ///     cluster carries it to the Agent beside that instance, which delivers it to that instance only.
    /// </summary>
    internal async Task SendCommandAsync(IServiceProvider instance, Pragmatic.ControlPlane.HostCommand command)
    {
        await using var agent = await OperatorConnectionAsync().ConfigureAwait(false);
        using var controlPlane = new Pragmatic.Agent.Client.AgentControlPlane(agent);
        var refused = await controlPlane.SendCommandAsync(HostIdOf(instance), command).ConfigureAwait(false);
        if (refused is not null)
            throw new InvalidOperationException($"The command was refused: {refused}");
    }

    /// <summary>Returns once the roster, read from the gateway's Agent, lists the instance in <paramref name="state" />.</summary>
    internal async Task UntilInStateAsync(IServiceProvider instance, Pragmatic.ControlPlane.HostState state)
    {
        await using var agent = await OperatorConnectionAsync().ConfigureAwait(false);
        using var controlPlane = new Pragmatic.Agent.Client.AgentControlPlane(agent);
        var hostId = HostIdOf(instance);
        await WarehouseWaits.UntilAsync(
            async () => (await controlPlane.GetAllHostsAsync().ConfigureAwait(false))
                .FirstOrDefault(host => host.HostId == hostId)?.State,
            listed => listed == state,
            $"the roster lists {hostId} as {state}").ConfigureAwait(false);
    }

    private static string HostIdOf(IServiceProvider instance)
        => instance.GetRequiredService<Pragmatic.ControlPlane.IHostIdentity>().HostId;

    private async Task<Pragmatic.Agent.Client.AgentConnection> OperatorConnectionAsync()
    {
        var agent = new Pragmatic.Agent.Client.AgentConnection(Agents["gateway"].SocketPath);
        await agent.ConnectAsync().ConfigureAwait(false);
        await agent.RegisterAsync("warehouse-suite-operator", "warehouse-suite-operator").ConfigureAwait(false);
        return agent;
    }

    /// <summary>The instance announcements each Agent holds, and what it printed, for a failure message.</summary>
    internal async Task<string> AnnouncementsByAgentAsync()
    {
        var report = new System.Text.StringBuilder();
        foreach (var agent in _agents)
        {
            await using var connection = new Pragmatic.Agent.Client.AgentConnection(agent.SocketPath);
            await connection.ConnectAsync().ConfigureAwait(false);
            await connection.RegisterAsync("warehouse-suite-diagnostics", "warehouse-suite-diagnostics").ConfigureAwait(false);
            var held = await connection.KvPrefixAsync(Pragmatic.Agent.Protocol.Payloads.InstanceAnnouncementKeys.Prefix)
                .ConfigureAwait(false);

            report.Append("Agent '").Append(agent.Name).Append("' holds ")
                .Append(held.Count.ToString(CultureInfo.InvariantCulture)).Append(" announcement(s): ")
                .AppendLine(string.Join(", ", held.Select(entry => entry.Key)));
            report.AppendLine(agent.Output);
        }

        return report.ToString();
    }

    /// <summary>A TCP port nothing holds at this moment.</summary>
    private static int FreeTcpPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>
    ///     A second Orders instance on its own port, connected where no Stock instance listens: what
    ///     placing an order meets when Stock is down. The caller disposes it.
    /// </summary>
    internal ServiceHost<OrdersHost> OrdersWithNobodyToAnswer()
        => new ServiceHost<OrdersHost>(OrdersSettings(_rabbit.NobodyAnswersConnectionString)).Start();

    private Dictionary<string, string?> OrdersSettings(string broker)
    {
        var settings = HostSettings("Orders", _postgres.OrdersConnectionString, Agents["orders"]);
        settings["Messaging:RabbitMq:ConnectionString"] = broker;
        settings["Messaging:RequestReplyTimeout"] = RequestReplyTimeout.ToString();
        return settings;
    }

    private Dictionary<string, string?> StockSettings(AgentDaemon agent)
    {
        var settings = HostSettings("Stock", _postgres.StockConnectionString, agent);
        settings["Messaging:RabbitMq:ConnectionString"] = _rabbit.ConnectionString;
        settings["Reservations:HoldSeconds"] = ((int)HoldFor.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        settings["Jobs:PollingIntervalSeconds"] = "1";
        settings["ConnectionStrings:Redis"] = _redis.GetConnectionString();
        return settings;
    }

    /// <summary>
    ///     How long a hold lasts in the suite. Long enough that the tests that place an order and read the
    ///     levels at once finish well inside it; short enough that the ones about its expiry wait seconds.
    /// </summary>
    internal static readonly TimeSpan HoldFor = TimeSpan.FromSeconds(6);

    /// <summary>Stops Orders and starts it again on the same port — the one its announcement names.</summary>
    internal async Task RestartOrdersAsync()
    {
        var (port, settings) = (Orders.Port, Orders.Settings);
        await Orders.DisposeAsync().ConfigureAwait(false);
        Orders = new ServiceHost<OrdersHost>(settings).Start(port);

        // Back when the gateway routes to it: the only Orders instance's route left with it.
        await UntilInRotationAsync("orders", Orders.Services).ConfigureAwait(false);
    }

    /// <summary>
    ///     Stops both Stock instances, runs <paramref name="whileStopped" />, and starts them again on the
    ///     same ports — the ones their announcements name.
    /// </summary>
    /// <remarks>
    ///     Both, because either one runs a due job: stopping only the instance that took the request would
    ///     prove the job is not in its memory, not that it outlives the service.
    /// </remarks>
    internal async Task RestartStockAsync(Func<Task> whileStopped)
    {
        var (portA, settingsA) = (StockA.Port, StockA.Settings);
        var (portB, settingsB) = (StockB.Port, StockB.Settings);

        await StockA.DisposeAsync().ConfigureAwait(false);
        await StockB.DisposeAsync().ConfigureAwait(false);

        await whileStopped().ConfigureAwait(false);

        StockA = new ServiceHost<StockHost>(settingsA).Start(portA);
        StockB = new ServiceHost<StockHost>(settingsB).Start(portB);

        // Back when the gateway routes to both: the route left with the last of them.
        await UntilInRotationAsync("warehouse", StockA.Services).ConfigureAwait(false);
        await UntilInRotationAsync("warehouse", StockB.Services).ConfigureAwait(false);
    }

    /// <summary>
    ///     Stops Stock instance B, runs <paramref name="whileStopped" />, and starts it again on the same
    ///     port — a new process to the Agents, with an instance id of its own.
    /// </summary>
    internal async Task RestartStockBAsync(Func<Task> whileStopped)
    {
        var (port, settings) = (StockB.Port, StockB.Settings);
        await StockB.DisposeAsync().ConfigureAwait(false);

        try
        {
            await whileStopped().ConfigureAwait(false);
        }
        finally
        {
            // Started again even when the test failed: the tests after it share the fixture.
            StockB = new ServiceHost<StockHost>(settings).Start(port);
        }

        await UntilInRotationAsync("warehouse", StockB.Services).ConfigureAwait(false);
    }

    /// <summary>What every host is given: its database, the token key, and the Agent beside it.</summary>
    private static Dictionary<string, string?> HostSettings(string service, string connectionString, AgentDaemon agent)
        => new()
        {
            [$"ConnectionStrings:{service}"] = connectionString,
            ["Jwt:Key"] = JwtKey,
            ["Jwt:Issuer"] = "warehouse",
            ["Jwt:Audience"] = "warehouse",
            ["Pragmatic:Agent:SocketPath"] = agent.SocketPath,
            // What one service publishes reaches the other within a second, not the default five.
            ["Messaging:OutboxPollingIntervalSeconds"] = "1",
        };

    public async Task DisposeAsync()
    {
        // The gateway first, then what it fronts, then what they lean on.
        await DisposeQuietlyAsync(Gateway).ConfigureAwait(false);
        await DisposeQuietlyAsync(Orders).ConfigureAwait(false);
        await DisposeQuietlyAsync(StockA).ConfigureAwait(false);
        await DisposeQuietlyAsync(StockB).ConfigureAwait(false);
        await DisposeQuietlyAsync(Shipping).ConfigureAwait(false);

        foreach (var agent in _agents)
            await agent.DisposeAsync().ConfigureAwait(false);

        await _postgres.DisposeAsync().ConfigureAwait(false);
        await _rabbit.DisposeAsync().ConfigureAwait(false);
        await _redis.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>A host that never started, because an earlier one failed, has nothing to dispose.</summary>
    private static async Task DisposeQuietlyAsync(IAsyncDisposable? host)
    {
        if (host is not null)
            await host.DisposeAsync().ConfigureAwait(false);
    }
}
