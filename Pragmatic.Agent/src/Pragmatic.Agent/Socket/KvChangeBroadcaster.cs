using System.Text.Json;
using System.Threading.Channels;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol;
using Pragmatic.Agent.Protocol.Payloads;

namespace Pragmatic.Agent.Socket;

/// <summary>
///     Bridges the KV store's change notifications to connected socket clients: every KV mutation is
///     pushed to apps as a <see cref="MessageType.KvChanged"/> frame. Without this pump the client's
///     Watch / StreamEvents / config- and flag-change subscriptions would never fire — the daemon
///     would only answer polling reads.
/// </summary>
/// <remarks>
///     <para>
///         Secret values are masked to <c>***</c> before broadcast — the same rule the KvPrefix
///         listing applies — so secret material is never pushed in the clear over the socket.
///     </para>
/// </remarks>
internal sealed class KvChangeBroadcaster : IDisposable
{
    private readonly AgentSocketServer _server;
    private readonly Channel<KvChangeEvent> _channel;
    private readonly IDisposable _subscription;
    private readonly CancellationTokenSource _cts = new();

    // Bound the queue so a slow/stalled client cannot make the daemon grow memory without limit.
    // DropOldest also keeps KvStore.NotifyWatchers' TryWrite from ever failing — a bounded channel in
    // the default (Wait) mode would return false when full, and NotifyWatchers drops the subscription
    // on a failed write, which would silently kill push for the daemon's lifetime.
    private const int QueueCapacity = 1024;

    // Cap a single broadcast so one client that stops reading its socket cannot wedge the pump and
    // stall KvChanged delivery to every other app.
    private static readonly TimeSpan BroadcastTimeout = TimeSpan.FromSeconds(5);

    public KvChangeBroadcaster(KvStore kvStore, AgentSocketServer server)
    {
        _server = server;
        _channel = Channel.CreateBounded<KvChangeEvent>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        // Empty prefix subscribes to every key; each client filters by prefix on its side.
        _subscription = kvStore.Watch(string.Empty, _channel);
        _ = PumpAsync(_cts.Token);
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var payload = new KvChangedPayload
                {
                    Key = evt.Key,
                    // Never push secret material in the clear (the stored value is ciphertext anyway).
                    Value = KvStore.IsSecretKey(evt.Key) ? "***" : evt.Value,
                    Version = evt.Version,
                    Deleted = evt.Deleted,
                };

                var message = new AgentMessage
                {
                    Type = MessageType.KvChanged,
                    Payload = JsonSerializer.SerializeToElement(payload),
                };

                // Bound each broadcast so a single client that stopped reading its socket cannot wedge
                // the pump forever. On timeout we drop this notification and move on — the client will
                // re-read current state on the next change it does observe (version-based convergence).
                using var broadcastCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                broadcastCts.CancelAfter(BroadcastTimeout);
                try
                {
                    await _server.BroadcastAsync(message, broadcastCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // daemon shutting down — exit the pump
                }
                catch (Exception ex)
                {
                    AgentLogger.Error("Socket", $"KvChanged broadcast failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — expected.
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _subscription.Dispose();
        _channel.Writer.TryComplete();
        _cts.Dispose();
    }
}
