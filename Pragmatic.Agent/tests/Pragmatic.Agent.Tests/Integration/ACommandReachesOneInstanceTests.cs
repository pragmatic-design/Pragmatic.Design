using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Socket;
using Pragmatic.ControlPlane;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     A command is addressed to an instance: of two instances of one app, only the one named
///     receives it.
/// </summary>
/// <remarks>
///     The roster lists instances, and <c>SendCommandAsync(targetHostId)</c> takes a host id as
///     <c>GetAllHostsAsync</c> returns it. A command keyed and delivered by app id would reach nobody
///     through a host id, and every instance through an app id: draining one Stock instance would drain both.
/// </remarks>
public sealed class ACommandReachesOneInstanceTests : IAsyncLifetime
{
    private readonly string _pipeName = $"pragmatic-test-{Guid.NewGuid():N}";
    private readonly KvStore _store = new();
    private readonly List<AgentConnection> _clients = [];
    private AgentSocketServer? _server;
    private CommandDispatchPump? _pump;

    public async Task InitializeAsync()
    {
        var handler = new AgentMessageHandler(_store, "test-agent");
        _server = new AgentSocketServer(_pipeName, handler);
        _server.Start();
        _pump = new CommandDispatchPump(_store, _server);
        handler.InstanceRegistered = _pump.FlushPendingFor;
        await Task.Delay(100);
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
            await client.DisposeAsync();
        _pump?.Dispose();
        _server?.Dispose();
    }

    [Fact]
    public async Task ACommandToInstanceB_ReachesB_AndNotA()
    {
        var (a, receivedByA) = await InstanceAsync("stock-a");
        var (b, receivedByB) = await InstanceAsync("stock-b");
        var producer = await RegisteredAsync("producer", "producer");
        using var controlPlane = new AgentControlPlane(producer);
        var command = new DrainCommand(TimeSpan.FromSeconds(1));

        (await controlPlane.SendCommandAsync("stock-b", command)).Should().BeNull();

        (await receivedByB.Task.WaitAsync(TimeSpan.FromSeconds(5))).CommandId.Should().Be(command.CommandId);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        receivedByA.Task.IsCompleted.Should().BeFalse("the command named B");
    }

    /// <summary>The control: a command for an instance that connects later waits for it, and reaches it then.</summary>
    [Fact]
    public async Task ACommandToAnInstanceNotYetConnected_ReachesItWhenItRegisters()
    {
        var producer = await RegisteredAsync("producer", "producer");
        using var controlPlane = new AgentControlPlane(producer);
        var command = new ExitMaintenanceCommand();
        await controlPlane.SendCommandAsync("stock-late", command);

        var (_, received) = await InstanceAsync("stock-late");

        (await received.Task.WaitAsync(TimeSpan.FromSeconds(5))).CommandId.Should().Be(command.CommandId);
    }

    private async Task<(AgentConnection Connection, TaskCompletionSource<CommandPayload> Received)> InstanceAsync(string instanceId)
    {
        var received = new TaskCompletionSource<CommandPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new AgentConnection(_pipeName);
        _clients.Add(connection);
        connection.OnCommand += payload => received.TrySetResult(payload);
        await connection.ConnectAsync();
        await connection.RegisterAsync("Warehouse.Stock.Host", "Warehouse.Stock.Host", instanceId: instanceId);
        return (connection, received);
    }

    private async Task<AgentConnection> RegisteredAsync(string appId, string instanceId)
    {
        var connection = new AgentConnection(_pipeName);
        _clients.Add(connection);
        await connection.ConnectAsync();
        await connection.RegisterAsync(appId, appId, instanceId: instanceId);
        return connection;
    }
}
