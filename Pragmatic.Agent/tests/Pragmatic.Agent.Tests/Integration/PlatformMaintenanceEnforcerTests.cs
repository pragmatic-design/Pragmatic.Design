using System.Collections.Concurrent;
using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Platform;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Socket;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     Wires the platform adapters: the daemon enforces per-app maintenance on the local platform. When an
///     app hosted here transitions into <c>Maintenance</c> (its <c>state/app:</c> descriptor), the detected
///     <see cref="IAgentPlatformAdapter"/> is invoked; a gossiped descriptor for an app NOT connected to
///     this daemon is ignored (that node's own daemon enforces it).
/// </summary>
public class PlatformMaintenanceEnforcerTests : IAsyncLifetime
{
    private readonly string _pipeName = $"pragmatic-plat-{Guid.NewGuid():N}";
    private readonly KvStore _kvStore = new();
    private readonly RecordingAdapter _adapter = new();
    private AgentSocketServer? _server;
    private PlatformMaintenanceEnforcer? _enforcer;
    private AgentConnection? _client;

    public async Task InitializeAsync()
    {
        var handler = new AgentMessageHandler(_kvStore, "test-agent");
        _server = new AgentSocketServer(_pipeName, handler);
        _server.Start();
        _enforcer = new PlatformMaintenanceEnforcer(_kvStore, _server, _adapter);
        await Task.Delay(100);

        _client = new AgentConnection(_pipeName);
        await _client.ConnectAsync();
        await _client.RegisterAsync("app-1", "App One", version: "1.0.0");
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
        _enforcer?.Dispose();
        _server?.Dispose();
    }

    [Fact]
    public async Task LocalApp_EntersAndLeavesMaintenance_DrivesAdapter()
    {
        // App is connected here → transition Ready→Maintenance activates the platform mechanism.
        _kvStore.Set(HostRosterKeys.Key("app-1", "i-1"), Descriptor("app-1", "Maintenance"));
        await WaitUntilAsync(() => _adapter.Activated.Contains("app-1"));

        // Back to Ready → deactivate.
        _kvStore.Set(HostRosterKeys.Key("app-1", "i-1"), Descriptor("app-1", "Ready"));
        await WaitUntilAsync(() => _adapter.Deactivated.Contains("app-1"));

        _adapter.Activated.Should().Contain("app-1");
        _adapter.Deactivated.Should().Contain("app-1");
    }

    [Fact]
    public async Task RemoteApp_NotConnectedHere_IsIgnored()
    {
        // No client for "remote-app" is connected to this daemon → the adapter must not fire for it.
        _kvStore.Set(HostRosterKeys.Key("remote-app", "i-1"), Descriptor("remote-app", "Maintenance"));
        await Task.Delay(400);

        _adapter.Activated.Should().NotContain("remote-app");
    }

    private static string Descriptor(string appId, string state) =>
        $$"""{"appId":"{{appId}}","appName":"{{appId}}","instanceId":"i-1","state":"{{state}}"}""";

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException("Condition not met within timeout.");
    }

    private sealed class RecordingAdapter : IAgentPlatformAdapter
    {
        public ConcurrentBag<string> Activated { get; } = [];
        public ConcurrentBag<string> Deactivated { get; } = [];

        public string PlatformName => "Recording";

        public Task ActivateMaintenanceAsync(string appId, CancellationToken ct = default)
        {
            Activated.Add(appId);
            return Task.CompletedTask;
        }

        public Task DeactivateMaintenanceAsync(string appId, CancellationToken ct = default)
        {
            Deactivated.Add(appId);
            return Task.CompletedTask;
        }

        public Task<bool> IsAppRunningAsync(string appId, CancellationToken ct = default) => Task.FromResult(true);

        public Task StopAppAsync(string appId, TimeSpan gracePeriod, CancellationToken ct = default) => Task.CompletedTask;

        public Task ReportHealthAsync(string appId, HealthStatus status, CancellationToken ct = default) => Task.CompletedTask;
    }
}
