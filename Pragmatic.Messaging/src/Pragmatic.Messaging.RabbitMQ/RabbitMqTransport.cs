using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Pragmatic.Messaging.RabbitMQ;

/// <summary>
///     RabbitMQ implementation of <see cref="IMessageTransport"/>.
///     Uses topic exchanges per boundary, queues per handler subscription.
/// </summary>
public sealed partial class RabbitMqTransport(RabbitMqOptions options, ILogger<RabbitMqTransport> logger)
    : IMessageTransport
{
    private readonly ConcurrentDictionary<string, bool> _declaredExchanges = new();
    private readonly ConcurrentDictionary<string, bool> _declaredQueues = new();
    private readonly List<RabbitMqSubscription> _subscriptions = [];
    // Protects _subscriptions against concurrent SubscribeAsync / DisconnectAsync.
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
    // Serializes all operations on the singleton _publishChannel. A single IChannel must
    // never be used concurrently — interleaved AMQP frames corrupt the protocol stream and
    // can tear down the connection. Covers exchange/queue declares and BasicPublishAsync.
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly ConnectionGate _gate = new();

    private IConnection? _connection;
    private IChannel? _publishChannel;
    private volatile TransportStatus _status = TransportStatus.Disconnected;
    private int _disposed;

    /// <inheritdoc />
    public string Name => "RabbitMQ";

    /// <inheritdoc />
    public TransportStatus Status => _status;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _gate.Begin();
        _status = TransportStatus.Connecting;
        LogConnecting(SafeEndpoint(options.ConnectionString));

        try
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(options.ConnectionString),
                AutomaticRecoveryEnabled = options.AutoReconnect,
                NetworkRecoveryInterval = TimeSpan.FromMilliseconds(options.ReconnectBaseDelayMs),
            };

            _connection = await factory.CreateConnectionAsync(ct).ConfigureAwait(false);
            // With confirms + tracking enabled, BasicPublishAsync completes only after the broker
            // confirms (and throws on nack) — no fire-and-forget loss between outbox and broker.
            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: options.PublisherConfirms,
                publisherConfirmationTrackingEnabled: options.PublisherConfirms);
            _publishChannel = await _connection.CreateChannelAsync(channelOptions, ct).ConfigureAwait(false);

            _status = TransportStatus.Connected;
            _gate.Opened();
            LogConnected();
        }
        catch (Exception ex)
        {
            _status = TransportStatus.Faulted;
            _gate.Failed(ex);
            LogConnectionFailed(ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        // Snapshot and clear subscriptions under lock so no concurrent SubscribeAsync
        // can add a new subscription after we start tearing down.
        List<RabbitMqSubscription> toDispose;
        await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            toDispose = new List<RabbitMqSubscription>(_subscriptions);
            _subscriptions.Clear();
        }
        finally
        {
            _subscriptionLock.Release();
        }

        foreach (var sub in toDispose)
            await sub.DisposeAsync().ConfigureAwait(false);

        if (_publishChannel is not null)
        {
            await _publishChannel.CloseAsync(ct).ConfigureAwait(false);
            _publishChannel.Dispose();
            _publishChannel = null;
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync(ct).ConfigureAwait(false);
            _connection.Dispose();
            _connection = null;
        }

        _status = TransportStatus.Disconnected;
        LogDisconnected();
    }

    /// <inheritdoc />
    public async Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);

        // Build properties
        var props = new BasicProperties
        {
            MessageId = context.MessageId,
            CorrelationId = context.CorrelationId,
            ContentType = "application/json",
            DeliveryMode = options.PersistentMessages ? DeliveryModes.Persistent : DeliveryModes.Transient,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = BuildHeaders(context),
        };

        // Serialize the declare + publish on the shared channel — concurrent use is unsafe.
        await _publishLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Ensure exchange exists (on _publishChannel — must stay under the lock)
            await EnsureExchangeAsync(topic, ct).ConfigureAwait(false);

            // Routing key = empty for fan-out (all bindings match)
            await _publishChannel!.BasicPublishAsync(
                exchange: topic,
                routingKey: "",
                mandatory: false,
                basicProperties: props,
                body: payload,
                cancellationToken: ct).ConfigureAwait(false);
        }
        finally
        {
            _publishLock.Release();
        }

        LogPublished(topic, context.MessageId);
    }

    /// <inheritdoc />
    public async Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);

        var props = new BasicProperties
        {
            MessageId = context.MessageId,
            CorrelationId = context.CorrelationId,
            ContentType = "application/json",
            DeliveryMode = options.PersistentMessages ? DeliveryModes.Persistent : DeliveryModes.Transient,
            Headers = BuildHeaders(context),
        };

        // Serialize the declare + publish on the shared channel — concurrent use is unsafe.
        await _publishLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Ensure queue exists (on _publishChannel — must stay under the lock)
            await EnsureQueueAsync(queue, ct).ConfigureAwait(false);

            // Direct to queue (default exchange, routing key = queue name)
            await _publishChannel!.BasicPublishAsync(
                exchange: "",
                routingKey: queue,
                mandatory: false,
                basicProperties: props,
                body: payload,
                cancellationToken: ct).ConfigureAwait(false);
        }
        finally
        {
            _publishLock.Release();
        }

        LogSent(queue, context.MessageId);
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable> SubscribeAsync(
        string topic,
        string subscriptionName,
        Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);

        // Loud, per-subscription warning: with no DLX the failure path below (nack without
        // requeue) makes RabbitMQ DISCARD the message permanently — a silent data-loss footgun
        // when left unconfigured in production.
        if (string.IsNullOrEmpty(options.DeadLetterExchange))
            LogNoDeadLetterExchange(subscriptionName);

        // Create dedicated channel for this consumer
        var consumerChannel = await _connection!.CreateChannelAsync(cancellationToken: ct).ConfigureAwait(false);
        await consumerChannel.BasicQosAsync(0, options.ConsumerPrefetchCount, false, ct).ConfigureAwait(false);

        // Ensure exchange on the subscriber's own channel (not _publishChannel) to avoid
        // topology races between the publisher and subscriber channels.
        await EnsureExchangeOnChannelAsync(consumerChannel, topic, ct).ConfigureAwait(false);
        await EnsureQueueOnChannelAsync(consumerChannel, subscriptionName, ct).ConfigureAwait(false);

        // Bind queue to exchange
        await consumerChannel.QueueBindAsync(subscriptionName, topic, "", cancellationToken: ct).ConfigureAwait(false);

        // Start consuming
        var consumer = new AsyncEventingBasicConsumer(consumerChannel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var context = ExtractContext(ea.BasicProperties);
                // Use CancellationToken.None for in-flight handlers: the outer `ct` is the
                // subscription setup token (host shutdown) — cancelling it mid-handler would
                // interrupt processing already in progress. Ack/Nack also use None so the
                // channel is not closed under us during graceful host shutdown.
                await handler(ea.Body, context, CancellationToken.None).ConfigureAwait(false);
                await consumerChannel.BasicAckAsync(ea.DeliveryTag, false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogConsumerError(subscriptionName, ex);
                // Nack without requeue. If RabbitMqOptions.DeadLetterExchange is configured, the queue's
                // x-dead-letter-exchange routes the message to the DLQ; otherwise RabbitMQ discards it.
                await consumerChannel.BasicNackAsync(ea.DeliveryTag, false, false, CancellationToken.None).ConfigureAwait(false);
            }
        };

        var consumerTag = await consumerChannel.BasicConsumeAsync(subscriptionName, false, consumer, ct).ConfigureAwait(false);

        LogSubscribed(subscriptionName, topic, consumerTag);

        var subscription = new RabbitMqSubscription(consumerChannel, consumerTag);
        await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _subscriptions.Add(subscription);
        }
        finally
        {
            _subscriptionLock.Release();
        }

        return subscription;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // The transport is registered under two service keys (IMessageTransport and the
        // concrete type), so the DI container disposes the same instance twice. Guard so
        // DisconnectAsync (which waits on the semaphores) never runs against disposed locks.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await DisconnectAsync().ConfigureAwait(false);
        _publishLock.Dispose();
        _subscriptionLock.Dispose();
    }

    /// <summary>
    ///     Connected, or waits for the connect in progress (<see cref="RabbitMqOptions.ConnectWaitTimeout" />):
    ///     the consumer service connects in the background, and an operation issued meanwhile is not a
    ///     mistake of its caller.
    /// </summary>
    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_status == TransportStatus.Connecting)
            await _gate.WaitAsync(Name, options.ConnectWaitTimeout, ct).ConfigureAwait(false);

        if (_status != TransportStatus.Connected || _connection is null || _publishChannel is null)
            throw new InvalidOperationException("RabbitMQ transport is not connected. Call ConnectAsync first.");
    }

    // =========================================================================
    // LoggerMessage
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Information, Message = "Connecting to RabbitMQ: {Endpoint}")]
    private partial void LogConnecting(string endpoint);

    /// <summary>
    ///     Returns a log-safe endpoint string (scheme, host, port, vhost) with any
    ///     userinfo stripped. RabbitMQ connection strings carry credentials in the
    ///     URI userinfo component — those must never reach the logs.
    /// </summary>
    private static string SafeEndpoint(string connectionString)
    {
        try
        {
            var uri = new Uri(connectionString);
            return $"{uri.Scheme}://{uri.Host}:{uri.Port}{uri.AbsolutePath}";
        }
        catch (UriFormatException)
        {
            return "(invalid connection string)";
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to RabbitMQ")]
    private partial void LogConnected();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscription '{Subscription}' has NO dead-letter exchange configured (RabbitMqOptions.DeadLetterExchange): a failed handler nacks WITHOUT requeue and RabbitMQ DISCARDS the message permanently. Configure a DLX for production.")]
    private partial void LogNoDeadLetterExchange(string subscription);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to connect to RabbitMQ")]
    private partial void LogConnectionFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disconnected from RabbitMQ")]
    private partial void LogDisconnected();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Published to exchange {Exchange} (id: {MessageId})")]
    private partial void LogPublished(string exchange, string messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sent to queue {Queue} (id: {MessageId})")]
    private partial void LogSent(string queue, string messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Subscribed {Subscription} to {Topic} (tag: {ConsumerTag})")]
    private partial void LogSubscribed(string subscription, string topic, string consumerTag);

    [LoggerMessage(Level = LogLevel.Error, Message = "Consumer {Subscription} error")]
    private partial void LogConsumerError(string subscription, Exception ex);

    // =========================================================================
    // Subscription Handle
    // =========================================================================

    private sealed class RabbitMqSubscription(IChannel channel, string consumerTag) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await channel.BasicCancelAsync(consumerTag).ConfigureAwait(false);
                await channel.CloseAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup
            }

            channel.Dispose();
        }
    }
}
