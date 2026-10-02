using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Socket;
using Pragmatic.ControlPlane;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     An instance that reports Draining leaves the rotation at once, and one that reports Ready
///     again is back in it: its announcement says so, and stays.
/// </summary>
/// <remarks>
///     Leaving it to the heartbeat is not enough: unless <c>ReportStatusAsync</c> changes the announcement, a
///     draining instance keeps receiving new requests until it stops, which is when its announcement goes.
/// </remarks>
public sealed class AnInstanceLeavesTheRotationWhenItReportsDrainingTests : IAsyncLifetime
{
    private readonly string _pipeName = $"pragmatic-test-{Guid.NewGuid():N}";
    private readonly KvStore _store = new();
    private readonly SettableHostStatus _status = new();
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
    public async Task ReportingDraining_TakesItOut_AndReportingReady_PutsItBack()
    {
        var options = new AgentOptions
        {
            SocketPath = _pipeName,
            Announce = new AgentRouteAnnouncement
            {
                RouteId = "warehouse", Path = "/warehouse/{**catch-all}", PathRemovePrefix = "/warehouse",
                Address = "http://127.0.0.1:5222",
            },
        };
        var services = new ServiceCollection();
        var connection = new AgentConnection(_pipeName);
        services.AddSingleton(connection);
        services.AddSingleton<IHostStatus>(_status);
        services.AddSingleton(sp => new AgentInstanceAnnouncer(connection, options));
        await using var provider = services.BuildServiceProvider();
        var instance = provider.GetRequiredService<AgentInstanceAnnouncer>();
        var heartbeat = new AgentHeartbeatService(connection, options, status: _status, announcer: instance);
        using var controlPlane = new AgentControlPlane(connection, provider);
        var key = InstanceAnnouncementKeys.Key("warehouse", instance.InstanceId);

        _status.TransitionTo(HostState.Ready);
        await heartbeat.StartAsync(CancellationToken.None);
        try
        {
            await UntilAsync(() => InRotation(key) is true);

            _status.TransitionTo(HostState.Draining);
            await controlPlane.ReportStatusAsync();
            InRotation(key).Should().BeFalse("the report itself takes it out, not a heartbeat later");

            _status.TransitionTo(HostState.Ready);
            await controlPlane.ReportStatusAsync();
            InRotation(key).Should().BeTrue();
        }
        finally
        {
            await heartbeat.StopAsync(CancellationToken.None);
        }
    }

    private bool? InRotation(string key)
        => _store.Get(key)?.Value is { } json
            ? JsonSerializer.Deserialize<InstanceAnnouncementPayload>(json)!.InRotation
            : null;

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
    }
}
