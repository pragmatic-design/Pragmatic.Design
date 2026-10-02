using System.Threading.Channels;
using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Socket;
using Pragmatic.ControlPlane;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     <see cref="AgentControlPlane.StreamEventsAsync" /> observed through a real daemon connection.
///     <para>
///         A <c>state/app:</c> value is a whole <c>HostDescriptorPayload</c> document, not a bare state
///         name. Parsing the document as if it were the state falls through to the fallback, so every
///         transition would surface as <c>Starting → Starting</c>.
///     </para>
///     <para>
///         <c>BroadcastEventAsync</c> writes under <c>events/</c>, so the stream must forward that prefix
///         alongside <c>state/app:</c> and <c>config/</c>, and the broadcast payload must keep every field
///         specific to the concrete event; otherwise the two halves of one API never meet.
///     </para>
///     These run against the real socket + <see cref="KvChangeBroadcaster" /> because the defect lives in
///     the seam between the two: a unit test over the mapping function alone would not have shown that
///     nothing was ever delivered.
/// </summary>
public class AgentControlPlaneEventStreamTests : IAsyncLifetime
{
    private readonly string _pipeName = $"pragmatic-cp-events-{Guid.NewGuid():N}";

    /// <summary>The instance the client registers as.</summary>
    private const string Instance = "cp-events-instance";
    private readonly KvStore _kvStore = new();
    private AgentSocketServer? _server;
    private KvChangeBroadcaster? _broadcaster;
    private AgentConnection? _client;
    private AgentControlPlane? _controlPlane;

    public async Task InitializeAsync()
    {
        var handler = new AgentMessageHandler(_kvStore, "test-agent");
        _server = new AgentSocketServer(_pipeName, handler);
        _server.Start();
        _broadcaster = new KvChangeBroadcaster(_kvStore, _server);
        await Task.Delay(100); // let the listener come up

        _client = new AgentConnection(_pipeName);
        await _client.ConnectAsync();
        await _client.RegisterAsync("cp-events", "Control Plane Events", version: "1.0.0", instanceId: Instance);
        _controlPlane = new AgentControlPlane(_client);
    }

    public async Task DisposeAsync()
    {
        _controlPlane?.Dispose();
        if (_client is not null)
            await _client.DisposeAsync();
        _broadcaster?.Dispose();
        _server?.Dispose();
    }

    /// <summary>
    ///     Starts consuming the event stream in the background and returns a reader plus the token source
    ///     that stops it. Consumption must be running <i>before</i> the mutation under test, otherwise the
    ///     KvChanged frame arrives with nobody subscribed.
    /// </summary>
    private (ChannelReader<ControlPlaneEvent> Reader, CancellationTokenSource Cts) StartStream()
    {
        var channel = Channel.CreateUnbounded<ControlPlaneEvent>();
        var cts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in _controlPlane!.StreamEventsAsync(cts.Token))
                    channel.Writer.TryWrite(evt);
            }
            catch (OperationCanceledException)
            {
                // Expected on teardown.
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        });

        return (channel.Reader, cts);
    }

    private static async Task<T> NextAsync<T>(ChannelReader<ControlPlaneEvent> reader, Func<T, bool> match)
        where T : ControlPlaneEvent
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var evt in reader.ReadAllAsync(timeout.Token))
        {
            if (evt is T typed && match(typed))
                return typed;
        }

        throw new InvalidOperationException($"Stream completed without a matching {typeof(T).Name}.");
    }

    [Fact]
    public async Task StateChange_ReportsTheRealStates_NotStartingToStarting()
    {
        var (reader, cts) = StartStream();
        using var _ = cts;

        // Give the subscription a moment to attach before mutating.
        await Task.Delay(200);

        // The daemon folds the reported state into the descriptor it stores under state/app:.
        await _client!.HeartbeatAsync("cp-events", state: "Maintenance");

        // The source is the instance, as the roster lists it, not the app.
        var evt = await NextAsync<HostStateChangedEvent>(reader, e => e.SourceHostId == Instance);

        evt.NewState.Should().Be(HostState.Maintenance);
        // Registration stored the host as Ready; that is the state it transitioned away from.
        evt.OldState.Should().Be(HostState.Ready);

        await cts.CancelAsync();
    }

    [Fact]
    public async Task BroadcastEvent_ReachesTheStream_WithItsOwnFields()
    {
        var (reader, cts) = StartStream();
        using var _ = cts;

        await Task.Delay(200);

        var published = new HostStateChangedEvent(
            SourceHostId: "remote-host",
            Timestamp: DateTimeOffset.UtcNow,
            OldState: HostState.Ready,
            NewState: HostState.Draining,
            Reason: "rolling deploy");

        await _controlPlane!.BroadcastEventAsync(published);

        var received = await NextAsync<HostStateChangedEvent>(reader, e => e.SourceHostId == "remote-host");

        received.OldState.Should().Be(HostState.Ready);
        received.NewState.Should().Be(HostState.Draining);
        received.Reason.Should().Be("rolling deploy");

        await cts.CancelAsync();
    }

    [Fact]
    public async Task BroadcastEvent_PreservesTheConcreteEventType()
    {
        var (reader, cts) = StartStream();
        using var _ = cts;

        await Task.Delay(200);

        var published = new ConfigChangedEvent(
            SourceHostId: "config-publisher",
            Timestamp: DateTimeOffset.UtcNow,
            Key: "features/beta",
            OldValue: "off",
            NewValue: "on",
            TenantId: "tenant-7");

        await _controlPlane!.BroadcastEventAsync(published);

        var received = await NextAsync<ConfigChangedEvent>(reader, e => e.SourceHostId == "config-publisher");

        received.Key.Should().Be("features/beta");
        received.OldValue.Should().Be("off");
        received.NewValue.Should().Be("on");
        received.TenantId.Should().Be("tenant-7");

        await cts.CancelAsync();
    }
}
