using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Messaging.Channels;

/// <summary>
///     In-process transport using <see cref="System.Threading.Channels.Channel{T}"/>.
///     Decouples producer from consumer with configurable backpressure.
///     Handler failures are persisted to <see cref="IDeadLetterStore"/> (when available)
///     instead of being lost with only a log line.
/// </summary>
public sealed partial class ChannelTransport(
    ChannelOptions options,
    ILogger<ChannelTransport> logger,
    IDeadLetterStore? deadLetterStore = null)
    : IMessageTransport
{
    // One entry per address, holding EVERY subscription to it — and each subscription owns its own
    // channel. Read lock-free on the publish path by replacing the array rather than mutating it.
    private readonly ConcurrentDictionary<string, ImmutableArray<ChannelSubscription>> _subscriptions = new();
    // Protects the read-modify-write of a subscription array (AddOrUpdate's factory can run twice).
    private readonly object _subscriptionLock = new();
    // Round-robin cursor for SendAsync, which delivers to exactly one of an address's consumers.
    private int _sendCursor;
    private volatile TransportStatus _status = TransportStatus.Disconnected;

    /// <inheritdoc />
    public string Name => "Channels";

    /// <inheritdoc />
    public TransportStatus Status => _status;

    /// <inheritdoc />
    /// <remarks>
    ///     ⚠️ <b>Every subscription of the topic gets a copy</b>, which is what a topic is for. One
    ///     channel per topic shared by publish and subscribe would not do it:
    ///     <c>System.Threading.Channels</c> readers <em>compete</em>, so a message would go to exactly one
    ///     subscription. N subscribers would behave as N competing consumers — and the one that received a
    ///     message of a type it does not handle drops it, because <c>TransportSubscriptionBinder</c>
    ///     acknowledges what is "another subscription's, already on its way there". It would not be on
    ///     its way anywhere. Nothing logged, nothing dead-lettered.
    ///     <para>
    ///         A publish to an address nobody subscribes to is <b>discarded</b>, as a topic exchange with
    ///         no bound queue discards. Keeping it for a subscriber that may arrive later would need the
    ///         shared channel, which is the mechanism ruled out above.
    ///     </para>
    /// </remarks>
    public async Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
    {
        if (!_subscriptions.TryGetValue(topic, out var subscribers) || subscribers.IsEmpty)
        {
            LogNobodySubscribed(topic);
            return;
        }

        var envelope = new ChannelEnvelope(payload, context);

        foreach (var subscriber in subscribers)
            await WriteAsync(subscriber, envelope, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Point-to-point: <b>one</b> consumer, chosen round-robin among the address's subscriptions, so
    ///     two workers on one queue share the load instead of each doing the work. The opposite of
    ///     <see cref="PublishAsync" />, and the reason the two cannot be the same method.
    /// </remarks>
    public async Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
    {
        if (!_subscriptions.TryGetValue(queue, out var consumers) || consumers.IsEmpty)
        {
            LogNobodySubscribed(queue);
            return;
        }

        var next = (int)((uint)Interlocked.Increment(ref _sendCursor) % (uint)consumers.Length);
        await WriteAsync(consumers[next], new ChannelEnvelope(payload, context), ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Hands the envelope to one subscription, tolerating one that is being disposed.
    /// </summary>
    /// <remarks>
    ///     A subscription completes its own writer when it is disposed, and it leaves the array a moment
    ///     later; a publish in between would otherwise throw into the caller for a subscriber that is
    ///     simply going away.
    /// </remarks>
    private static async Task WriteAsync(ChannelSubscription subscription, ChannelEnvelope envelope, CancellationToken ct)
    {
        try
        {
            await subscription.Channel.Writer.WriteAsync(envelope, ct).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // The subscription is shutting down; its consumer loop is already stopping.
        }
    }

    /// <inheritdoc />
    public Task<IAsyncDisposable> SubscribeAsync(
        string topic,
        string subscriptionName,
        Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        // This subscription's OWN channel: the fan-out lives here rather than in the publisher, so N
        // consumer tasks of one subscription still compete for its messages while N subscriptions of
        // one topic each get their own copy.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var subscription = new ChannelSubscription(subscriptionName, cts, CreateChannel());
        var channel = subscription.Channel;

        // Start N consumer tasks and track them on the subscription so that
        // Disconnect/Dispose can await graceful completion — without this
        // handler callbacks could fire after the pipeline was disposed.
        for (var i = 0; i < options.ConsumerCount; i++)
        {
            var consumerId = i;
            var consumerTask = Task.Run(async () =>
            {
                LogConsumerStarted(subscriptionName, consumerId, topic);
                try
                {
                    await foreach (var envelope in channel.Reader.ReadAllAsync(cts.Token).ConfigureAwait(false))
                    {
                        try
                        {
                            await handler(envelope.Payload, envelope.Context, cts.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            LogHandlerError(subscriptionName, consumerId, ex);
                            await DeadLetterAsync(envelope, topic, ex).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown
                }

                LogConsumerStopped(subscriptionName, consumerId, topic);
            }, cts.Token);

            subscription.TrackTask(consumerTask);
        }

        // Lock the read-modify-write: AddOrUpdate's factories can run more than once under contention,
        // and a lost update here is a subscription that silently receives nothing.
        lock (_subscriptionLock)
        {
            _subscriptions[topic] = _subscriptions.TryGetValue(topic, out var existing)
                ? existing.Add(subscription)
                : [subscription];
        }

        IAsyncDisposable disposable = subscription;
        return Task.FromResult(disposable);
    }

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken ct = default)
    {
        _status = TransportStatus.Connected;
        LogConnected();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _status = TransportStatus.Disconnected;

        // Each subscription owns its channel, so completing and cancelling are one walk.
        foreach (var kvp in _subscriptions)
        foreach (var sub in kvp.Value)
            await sub.DisposeAsync().ConfigureAwait(false);

        _subscriptions.Clear();
        LogDisconnected();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Persists a message whose handler threw. Best-effort: a dead-letter store failure is
    ///     logged, never rethrown (the consumer loop must keep draining). The transport only sees
    ///     bytes, so the payload is stored as UTF-8 JSON and the channel/topic stands in for the
    ///     message type.
    /// </summary>
    private async Task DeadLetterAsync(ChannelEnvelope envelope, string topic, Exception error)
    {
        if (deadLetterStore is null) return;

        try
        {
            var payload = System.Text.Encoding.UTF8.GetString(envelope.Payload.Span);
            await deadLetterStore.StoreAsync(new DeadLetterMessage(
                MessageType: topic,
                Payload: payload,
                Error: error.Message,
                RetryCount: envelope.Context.RetryCount,
                Context: envelope.Context,
                FailedAt: DateTimeOffset.UtcNow)).ConfigureAwait(false);

            Diagnostics.MessagingDiagnostics.DeadLettered.Add(1);
        }
        catch (Exception storeEx)
        {
            LogDeadLetterFailed(topic, storeEx);
        }
    }

    /// <summary>One subscription's buffer. Bounded per subscription, so a slow one backs up alone.</summary>
    /// <remarks>
    ///     ⚠️ The capacity is now <b>per subscription</b> rather than per address, so N subscribers to a
    ///     topic hold N × <see cref="ChannelOptions.Capacity" /> messages at worst. That is the price of
    ///     a copy each, and it is the same shape a broker has (a queue per subscription). With
    ///     <c>FullMode = Wait</c> one slow subscriber now blocks the publisher for that address, where
    ///     before it shared one buffer with everybody.
    /// </remarks>
    private Channel<ChannelEnvelope> CreateChannel()
        => Channel.CreateBounded<ChannelEnvelope>(
            new BoundedChannelOptions(options.Capacity)
            {
                FullMode = options.FullMode,
                SingleReader = options.ConsumerCount == 1,
                SingleWriter = false
            });

    /// <summary>Returns the pending message count for an address (for health checks).</summary>
    /// <remarks>The sum across its subscriptions, each of which now holds its own copy.</remarks>
    internal int GetPendingCount(string channelName)
    {
        if (!_subscriptions.TryGetValue(channelName, out var subscribers))
            return 0;

        var pending = 0;
        foreach (var subscriber in subscribers)
            pending += subscriber.Channel.Reader.Count;

        return pending;
    }

    /// <summary>Returns all subscribed addresses (for health checks).</summary>
    internal IReadOnlyCollection<string> GetChannelNames() => [.. _subscriptions.Keys];

    /// <summary>The bounded capacity per channel (for the health-check saturation ratio).</summary>
    internal int Capacity => options.Capacity;

    [LoggerMessage(Level = LogLevel.Information, Message = "Channel transport connected")]
    private partial void LogConnected();

    [LoggerMessage(Level = LogLevel.Information, Message = "Channel transport disconnected")]
    private partial void LogDisconnected();

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Consumer {SubscriptionName}[{ConsumerId}] started on channel {Topic}")]
    private partial void LogConsumerStarted(string subscriptionName, int consumerId, string topic);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Consumer {SubscriptionName}[{ConsumerId}] stopped on channel {Topic}")]
    private partial void LogConsumerStopped(string subscriptionName, int consumerId, string topic);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Consumer {SubscriptionName}[{ConsumerId}] handler error")]
    private partial void LogHandlerError(string subscriptionName, int consumerId, Exception ex);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed to dead-letter a message from channel {Topic}; the message is lost")]
    private partial void LogDeadLetterFailed(string topic, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Nothing subscribes to {Topic}; the message was discarded, as a broker discards what no queue is bound to")]
    private partial void LogNobodySubscribed(string topic);

    private sealed class ChannelSubscription(
        string name, CancellationTokenSource cts, Channel<ChannelEnvelope> channel) : IAsyncDisposable
    {
        public string Name => name;

        /// <summary>This subscription's own buffer — the thing that makes a topic fan out.</summary>
        public Channel<ChannelEnvelope> Channel => channel;

        private readonly List<Task> _consumerTasks = new();
        private int _disposed;

        internal void TrackTask(Task task) => _consumerTasks.Add(task);

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            // Complete before cancelling so a publisher blocked on a full buffer is released rather
            // than left awaiting a writer nobody will ever read from again.
            channel.Writer.TryComplete();

            try { cts.Cancel(); } catch (ObjectDisposedException) { }

            // Await consumer loops so no handler runs after dispose completes.
            // Tasks swallow OperationCanceledException internally — Task.WhenAll
            // will still surface any unexpected exception, which we intentionally
            // swallow here (logging happens inside the consumer loop).
            try
            {
                await Task.WhenAll(_consumerTasks).ConfigureAwait(false);
            }
            catch
            {
                // Consumer loops already logged their own errors.
            }

            try { cts.Dispose(); } catch (ObjectDisposedException) { }
        }
    }
}
