using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Socket;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     BUG-A1 regression: the daemon must push KV mutations to connected socket clients as
///     <c>KvChanged</c> frames. Before <see cref="KvChangeBroadcaster" /> was wired, <c>BroadcastAsync</c>
///     had zero callers, so a client's Watch/StreamEvents/config-change subscriptions never fired and the
///     daemon only answered polling reads. These drive a real IPC connection (named pipe / UDS) end to end
///     and assert a frame actually arrives — and that secret material is masked before it leaves the daemon.
/// </summary>
public class KvChangeBroadcasterIntegrationTests : IAsyncLifetime
{
    private readonly string _pipeName = $"pragmatic-kvpush-{Guid.NewGuid():N}";
    private readonly KvStore _kvStore = new();
    private AgentSocketServer? _server;
    private KvChangeBroadcaster? _broadcaster;
    private AgentConnection? _client;

    public async Task InitializeAsync()
    {
        var handler = new AgentMessageHandler(_kvStore, "test-agent");
        _server = new AgentSocketServer(_pipeName, handler);
        _server.Start();
        _broadcaster = new KvChangeBroadcaster(_kvStore, _server);
        await Task.Delay(100); // let the listener come up

        _client = new AgentConnection(_pipeName);
        await _client.ConnectAsync();
        await _client.RegisterAsync("push-test", "Push Test", version: "1.0.0");
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
        _broadcaster?.Dispose();
        _server?.Dispose();
    }

    [Fact]
    public async Task KvMutation_PushesKvChangedFrame_ToConnectedClient()
    {
        // Filter by key: the broadcaster watches the empty prefix, so registration-time writes under
        // state/app: also emit frames — latch only on the key this test mutates.
        var received = new TaskCompletionSource<KvChangedPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        _client!.OnKvChanged += p =>
        {
            if (p.Key == "config/host")
                received.TrySetResult(p);
        };

        _kvStore.Set("config/host", "localhost");

        var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pushed.Value.Should().Be("localhost");
        pushed.Deleted.Should().BeFalse();
        pushed.Version.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SecretKeyMutation_PushesMaskedValue()
    {
        var received = new TaskCompletionSource<KvChangedPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        _client!.OnKvChanged += p =>
        {
            if (p.Key == "secret/api-token")
                received.TrySetResult(p);
        };

        _kvStore.Set("secret/api-token", "super-sensitive");

        // Secret material must never leave the daemon in the clear — the broadcaster masks it to "***".
        var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pushed.Value.Should().Be("***");
        pushed.Value.Should().NotContain("super-sensitive");
    }
}
