using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Socket;
using Pragmatic.ControlPlane;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     End-to-end tests for the P2 Agent backbone: host lifecycle roster (state/app:), command
///     delivery over the socket (producer → KV → daemon pump → target app), at-least-once + de-dup,
///     and KV-lease cluster leadership. Real client ↔ daemon over a named pipe / Unix socket.
/// </summary>
public class AgentBackboneIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    private readonly string _pipeName = $"pragmatic-backbone-{Guid.NewGuid():N}";
    private readonly KvStore _kvStore = new();
    private readonly List<AgentConnection> _clients = [];
    private AgentSocketServer? _server;
    private CommandDispatchPump? _pump;

    public async Task InitializeAsync()
    {
        var handler = new AgentMessageHandler(_kvStore, "test-agent");
        _server = new AgentSocketServer(_pipeName, handler);
        _server.Start();
        _pump = new CommandDispatchPump(_kvStore, _server);
        handler.InstanceRegistered = _pump.FlushPendingFor;

        await Task.Delay(100); // let the listener come up
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
            await client.DisposeAsync();
        _pump?.Dispose();
        _server?.Dispose();
    }

    private async Task<AgentConnection> ConnectAsync(string appId, string appName, string? hostType = null)
    {
        var client = new AgentConnection(_pipeName);
        _clients.Add(client);
        await client.ConnectAsync();
        // The instance under the app's own name: a command is addressed to an instance.
        await client.RegisterAsync(appId, appName, version: "1.0.0", hostType: hostType,
            startedAt: DateTimeOffset.UtcNow, instanceId: appId);
        return client;
    }

    [Fact]
    public async Task GetAllHosts_ReturnsRealRoster_WithTypeAndTimestamps()
    {
        await ConnectAsync("host-a", "Host A", hostType: "Worker");
        await ConnectAsync("host-b", "Host B", hostType: "Tenant");
        var reader = await ConnectAsync("reader", "Reader");

        using var cp = new AgentControlPlane(reader);
        var hosts = await cp.GetAllHostsAsync();

        // HostId is the instance; these register none, so the Agent names each connection.
        hosts.Should().Contain(h => h.HostName == "Host A" && h.HostType == HostType.Worker);
        hosts.Should().Contain(h => h.HostName == "Host B" && h.HostType == HostType.Tenant);
        hosts.Select(h => h.HostId).Should().OnlyHaveUniqueItems();
        hosts.Should().OnlyContain(h => h.State == HostState.Ready);
    }

    [Fact]
    public async Task HostDisconnect_RemovesRosterEntry()
    {
        var reader = await ConnectAsync("reader", "Reader");
        var leaving = await ConnectAsync("leaving", "Leaving Host");

        using var cp = new AgentControlPlane(reader);
        (await cp.GetAllHostsAsync()).Should().Contain(h => h.HostName == "Leaving Host");

        await leaving.DisposeAsync();

        await WaitUntilAsync(async () => (await cp.GetAllHostsAsync()).All(h => h.HostName != "Leaving Host"));
        (await cp.GetAllHostsAsync()).Should().NotContain(h => h.HostName == "Leaving Host");
    }

    [Fact]
    public async Task SendCommand_DeliversLosslessly_ToTargetApp()
    {
        var producer = await ConnectAsync("producer", "Producer");
        var target = await ConnectAsync("target", "Target");

        var tcs = new TaskCompletionSource<CommandPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        target.OnCommand += p => tcs.TrySetResult(p);

        using var cp = new AgentControlPlane(producer);
        var command = new DrainCommand(TimeSpan.FromSeconds(30));
        var error = await cp.SendCommandAsync("target", command);
        error.Should().BeNull();

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        received.CommandType.Should().Be("DrainCommand");
        received.CommandId.Should().Be(command.CommandId);
        received.TargetHostId.Should().Be("target");

        // Lossless: the full command round-trips through the KV envelope, fields intact.
        var back = JsonSerializer.Deserialize<DrainCommand>(received.Data!.Value.GetRawText(), CaseInsensitive);
        back!.GracePeriod.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task SendCommand_ToDisconnectedApp_DeliversOnReconnect()
    {
        var producer = await ConnectAsync("producer", "Producer");

        // Target is not connected yet — the command must wait in KV and flush on register.
        using var cp = new AgentControlPlane(producer);
        var command = new EnterMaintenanceCommand("scheduled");
        await cp.SendCommandAsync("late-target", command);

        var target = new AgentConnection(_pipeName);
        _clients.Add(target);
        var tcs = new TaskCompletionSource<CommandPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        target.OnCommand += p => tcs.TrySetResult(p);
        await target.ConnectAsync();
        await target.RegisterAsync("late-target", "Late Target", instanceId: "late-target");

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        received.CommandId.Should().Be(command.CommandId);
    }

    [Fact]
    public async Task RedeliveredCommand_IsDeDuplicated_ExecutesOnce()
    {
        var producer = await ConnectAsync("producer", "Producer");
        var target = await ConnectAsync("dedup-target", "Target");

        var dispatcher = new CountingDispatcher();
        using var targetCp = new AgentControlPlane(target, new SingleServiceProvider(dispatcher));

        using var senderCp = new AgentControlPlane(producer);
        var command = new ExitMaintenanceCommand(); // stable CommandId across both sends

        await senderCp.SendCommandAsync("dedup-target", command);
        await WaitUntilAsync(() => Task.FromResult(dispatcher.Count == 1));

        // Same command id again → at-least-once transport re-delivers → de-dup drops it.
        await senderCp.SendCommandAsync("dedup-target", command);
        await Task.Delay(500);

        dispatcher.Count.Should().Be(1);
    }

    [Fact]
    public async Task ClusterLeadership_SingleLeaderPerScope_HandsOffOnRelease()
    {
        var a = await ConnectAsync("leader-a", "A");
        var b = await ConnectAsync("leader-b", "B");

        await using var la = new AgentClusterLeadership(a, new AgentOptions { AppId = "leader-a" });
        await using var lb = new AgentClusterLeadership(b, new AgentOptions { AppId = "leader-b" });

        (await la.TryAcquireAsync("deploy")).Should().BeTrue();
        la.IsLeader("deploy").Should().BeTrue();

        // B cannot steal a live lease.
        (await lb.TryAcquireAsync("deploy")).Should().BeFalse();
        lb.IsLeader("deploy").Should().BeFalse();

        // After A releases, B can claim it.
        await la.ReleaseAsync("deploy");
        la.IsLeader("deploy").Should().BeFalse();
        (await lb.TryAcquireAsync("deploy")).Should().BeTrue();
        lb.IsLeader("deploy").Should().BeTrue();
    }

    [Fact]
    public async Task ClusterLeadership_StealsExpiredForeignLease()
    {
        var host = await ConnectAsync("steal-host", "Steal Host");
        await using var leadership = new AgentClusterLeadership(host, new AgentOptions { AppId = "steal-host" });

        // A crashed host left an EXPIRED lease behind (leaseUntil in the past, its renew loop gone).
        _kvStore.Set("leader/deploy",
            """{"hostId":"dead-host","leaseUntil":"2000-01-01T00:00:00+00:00"}""");

        // Any host may reclaim an expired lease — the takeover property that makes the KV-lease election
        // resilient to a dead leader. (A LIVE foreign lease is non-stealable — covered by HandsOffOnRelease.)
        (await leadership.TryAcquireAsync("deploy")).Should().BeTrue();
        leadership.IsLeader("deploy").Should().BeTrue();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException("Condition not met within timeout.");
    }

    private sealed class CountingDispatcher : IHostCommandDispatcher
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public Task DispatchAsync(string commandType, string commandJson, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _count);
            return Task.CompletedTask;
        }
    }

    private sealed class SingleServiceProvider(IHostCommandDispatcher dispatcher) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IHostCommandDispatcher) ? dispatcher : null;
    }
}
