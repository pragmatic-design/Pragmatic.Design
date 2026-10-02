using System.Text.Json;
using System.Threading.Channels;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol;

namespace Pragmatic.Agent.Socket;

/// <summary>
///     Delivers host commands from the KV store to locally-connected instances. A producer writes
///     <c>commands/{instanceId}/{commandId}</c> = a serialized command envelope; this pump watches that
///     namespace and pushes a <see cref="MessageType.Command"/> frame to the target instance when it is
///     connected to <em>this</em> daemon. Delivery is at-least-once — the receiver de-duplicates by
///     command id and, on success, writes <c>commands-ack/{instanceId}/{commandId}</c>, which this pump
///     observes to delete the delivered command key. A periodic sweep reclaims command keys for instances
///     that never connect (no ACK ever arrives), which the absence of a KV TTL would otherwise leak.
/// </summary>
/// <remarks>
///     Keyed by instance, not app: with two instances of one app, a command keyed by the app
///     reached both — draining one drained the other.
/// </remarks>
internal sealed class CommandDispatchPump : IDisposable
{
    private const string CommandPrefix = "commands/";
    private const string AckPrefix = "commands-ack/";

    // Bounded + DropOldest for the same reason as KvChangeBroadcaster: a stalled reader must never make
    // KvStore.NotifyWatchers drop the subscription (which would silently kill delivery for good).
    private const int QueueCapacity = 1024;
    private static readonly TimeSpan DeliverTimeout = TimeSpan.FromSeconds(5);

    // Command keys with no ACK after this long are swept — covers apps that never (re)connect.
    private static readonly TimeSpan StaleCommandTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly KvStore _kvStore;
    private readonly AgentSocketServer _server;
    private readonly Channel<KvChangeEvent> _channel;
    private readonly IDisposable _subscription;
    private readonly CancellationTokenSource _cts = new();

    public CommandDispatchPump(KvStore kvStore, AgentSocketServer server)
    {
        _kvStore = kvStore;
        _server = server;
        _channel = Channel.CreateBounded<KvChangeEvent>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        // Prefix "commands" matches both "commands/" (deliveries) and "commands-ack/" (cleanup signals).
        _subscription = kvStore.Watch("commands", _channel);
        _ = PumpAsync(_cts.Token);
        _ = SweepLoopAsync(_cts.Token);
    }

    /// <summary>
    ///     Pushes every command currently queued for <paramref name="instanceId"/> to it. Called when an
    ///     instance registers so commands written while it was disconnected are delivered on (re)connect (the
    ///     watch only fires on <em>changes</em>, so a pre-existing key would otherwise never be pushed).
    /// </summary>
    public void FlushPendingFor(string instanceId)
    {
        var prefix = $"{CommandPrefix}{instanceId}/";
        foreach (var entry in _kvStore.GetByPrefix(prefix))
        {
            _ = DeliverAsync(instanceId, entry.Value, _cts.Token);
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (evt.Key.StartsWith(AckPrefix, StringComparison.Ordinal))
                {
                    if (!evt.Deleted)
                        HandleAck(evt.Key);
                    continue;
                }

                if (!evt.Key.StartsWith(CommandPrefix, StringComparison.Ordinal) || evt.Deleted)
                    continue;

                var instanceId = ExtractTarget(evt.Key, CommandPrefix);
                if (instanceId is not null)
                    await DeliverAsync(instanceId, evt.Value, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — expected.
        }
    }

    private async Task DeliverAsync(string instanceId, string? value, CancellationToken ct)
    {
        if (value is null)
            return;

        JsonElement payload;
        try
        {
            payload = JsonSerializer.Deserialize<JsonElement>(value);
        }
        catch (JsonException)
        {
            return; // Garbled command value — skip (the sweep will reclaim the key).
        }

        var message = new AgentMessage
        {
            Type = MessageType.Command,
            Payload = payload,
        };

        // SendToInstanceAsync is a no-op if the instance is not locally connected; the command stays in KV
        // and is flushed on the instance's next register. Bound the send so a wedged client cannot stall
        // the pump.
        using var deliverCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deliverCts.CancelAfter(DeliverTimeout);
        try
        {
            await _server.SendToInstanceAsync(instanceId, message, deliverCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AgentLogger.Error("Socket", $"Command delivery to {instanceId} failed: {ex.Message}");
        }
    }

    private void HandleAck(string ackKey)
    {
        // ackKey = "commands-ack/{instanceId}/{commandId}" → delete the matching command key + the ack itself.
        var rest = ackKey[AckPrefix.Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == rest.Length - 1)
            return;

        var instanceId = rest[..slash];
        var commandId = rest[(slash + 1)..];
        _kvStore.Delete($"{CommandPrefix}{instanceId}/{commandId}");
        _kvStore.Delete(ackKey);
    }

    private async Task SweepLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(SweepInterval, ct).ConfigureAwait(false);
                SweepStaleCommands();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — expected.
        }
    }

    private void SweepStaleCommands()
    {
        var cutoff = DateTimeOffset.UtcNow - StaleCommandTtl;
        foreach (var entry in _kvStore.GetByPrefix(CommandPrefix))
        {
            if (entry.UpdatedAt < cutoff)
                _kvStore.Delete(entry.Key);
        }
    }

    private static string? ExtractTarget(string key, string prefix)
    {
        var rest = key[prefix.Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        return slash <= 0 ? null : rest[..slash];
    }

    public void Dispose()
    {
        _cts.Cancel();
        _subscription.Dispose();
        _channel.Writer.TryComplete();
        _cts.Dispose();
    }
}
