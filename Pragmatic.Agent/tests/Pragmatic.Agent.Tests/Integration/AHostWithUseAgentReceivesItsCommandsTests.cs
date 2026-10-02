using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Socket;
using Pragmatic.Composition;
using Pragmatic.ControlPlane;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     A host started with <c>UseAgent()</c> receives the commands the Agent delivers to it.
/// </summary>
/// <remarks>
///     <c>AgentControlPlane</c> listens for commands from its constructor, and the only services that take
///     <c>IControlPlane</c> are the command handlers, resolved after a command arrives. Unless the host builds
///     the control plane when it starts, every delivered command is dropped. A test that builds the control
///     plane by hand cannot see that, so this one goes through <c>UseAgent()</c>.
/// </remarks>
public sealed class AHostWithUseAgentReceivesItsCommandsTests : IAsyncLifetime
{
    private const string Instance = "stock-host-1";

    private readonly string _pipeName = $"pragmatic-test-{Guid.NewGuid():N}";
    private readonly KvStore _store = new();
    private readonly RecordingDispatcher _dispatcher = new();
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

    public Task DisposeAsync()
    {
        _pump?.Dispose();
        _server?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ACommandSentToTheHostsInstance_ReachesItsDispatcher()
    {
        using var host = HostWithAgent();
        await host.StartAsync();
        try
        {
            await UntilAsync(() => _store.Get(HostRosterKeys.Key("Stock.Host", Instance)) is not null);

            await SendAsync(Instance, new ExitMaintenanceCommand());

            await UntilAsync(() => _dispatcher.Received.Contains(nameof(ExitMaintenanceCommand)));
            _dispatcher.Received.Should().Contain(nameof(ExitMaintenanceCommand));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>The control: a command for another instance does not reach this host.</summary>
    [Fact]
    public async Task ACommandSentToAnotherInstance_DoesNotReachIt()
    {
        using var host = HostWithAgent();
        await host.StartAsync();
        try
        {
            await UntilAsync(() => _store.Get(HostRosterKeys.Key("Stock.Host", Instance)) is not null);

            await SendAsync("another-instance", new ExitMaintenanceCommand());
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            _dispatcher.Received.Should().BeEmpty();
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private IHost HostWithAgent()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Pragmatic:Agent:SocketPath"] = _pipeName;
        builder.Services.AddSingleton<IHostIdentity>(new Identity());
        builder.Services.AddSingleton<IHostCommandDispatcher>(_dispatcher);
        new Builder(builder).UseAgent();
        return builder.Build();
    }

    private async Task SendAsync(string instance, HostCommand command)
    {
        await using var sender = new AgentConnection(_pipeName);
        await sender.ConnectAsync();
        await sender.RegisterAsync("operator", "operator", instanceId: "operator");
        using var controlPlane = new AgentControlPlane(sender);
        (await controlPlane.SendCommandAsync(instance, command)).Should().BeNull();
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
    }

    private sealed class RecordingDispatcher : IHostCommandDispatcher
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _received = new();

        public IReadOnlyCollection<string> Received => _received;

        public Task DispatchAsync(string commandType, string commandJson, CancellationToken ct = default)
        {
            _received.Enqueue(commandType);
            return Task.CompletedTask;
        }
    }

    private sealed record Identity : IHostIdentity
    {
        public string HostId => Instance;
        public string HostName => "Stock.Host";
        public HostType HostType => HostType.Tenant;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class Builder(HostApplicationBuilder host) : IPragmaticBuilder
    {
        public IServiceCollection Services => host.Services;
        public IConfiguration Configuration => host.Configuration;
        public IHostEnvironment Environment => host.Environment;
    }
}
