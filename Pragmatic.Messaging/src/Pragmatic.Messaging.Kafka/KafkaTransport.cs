using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Diagnostics;

namespace Pragmatic.Messaging.Kafka;

/// <summary>
///     Apache Kafka implementation of <see cref="IMessageTransport"/>.
///     Uses consumer groups for competing consumers, partition assignment for ordering.
/// </summary>
public sealed partial class KafkaTransport(KafkaOptions options, ILogger<KafkaTransport> logger) : IMessageTransport
{
    // Maps subscription name → (consumer, cancellationTokenSource) for coordinated shutdown.
    // The consumer is added BEFORE the background Task starts so DisconnectAsync cannot miss it.
    private readonly ConcurrentDictionary<string, (IConsumer<string, byte[]> Consumer, CancellationTokenSource Cts)> _consumers = new();

    private readonly ConcurrentDictionary<string, bool> _ensuredTopics = new();

    private IProducer<string, byte[]>? _producer;
    private IAdminClient? _adminClient;
    private volatile TransportStatus _status = TransportStatus.Disconnected;

    /// <inheritdoc />
    public string Name => "Kafka";

    /// <inheritdoc />
    public TransportStatus Status => _status;

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken ct = default)
    {
        _status = TransportStatus.Connecting;

        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            EnableIdempotence = options.EnableIdempotence,
            Acks = Acks.All,
        };

        _producer = new ProducerBuilder<string, byte[]>(config).Build();

        if (options.AutoCreateTopics)
        {
            _adminClient = new AdminClientBuilder(
                new AdminClientConfig { BootstrapServers = options.BootstrapServers }).Build();
        }

        _status = TransportStatus.Connected;
        LogConnected(options.BootstrapServers);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Creates the topic explicitly (idempotent, cached) so the transport works against
    ///     production brokers where <c>auto.create.topics.enable</c> is off. A concurrent-create
    ///     race resolves as TopicAlreadyExists and is treated as success.
    /// </summary>
    private async Task EnsureTopicAsync(string topic)
    {
        if (_adminClient is null || _ensuredTopics.ContainsKey(topic))
            return;

        try
        {
            await _adminClient.CreateTopicsAsync([
                new TopicSpecification
                {
                    Name = topic,
                    NumPartitions = options.DefaultPartitions,
                    ReplicationFactor = options.ReplicationFactor,
                },
            ]).ConfigureAwait(false);
            LogTopicCreated(topic, options.DefaultPartitions);
        }
        catch (CreateTopicsException ex) when (
            ex.Results.All(r => r.Error.Code is ErrorCode.TopicAlreadyExists or ErrorCode.NoError))
        {
            // Already there (or concurrently created) — exactly what we want.
        }

        _ensuredTopics.TryAdd(topic, true);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        // Cancel every consumer loop first, then dispose.  This ordering prevents
        // consumer methods (Commit, Close) from being called after Dispose().
        foreach (var entry in _consumers.Values)
        {
            entry.Cts.Cancel();
        }
        // Give consumer loops a moment to exit their poll before disposing.
        await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
        foreach (var entry in _consumers.Values)
        {
            try { entry.Consumer.Close(); } catch { /* best-effort */ }
            entry.Consumer.Dispose();
            entry.Cts.Dispose();
        }
        _consumers.Clear();

        if (_producer is not null)
        {
            _producer.Flush(TimeSpan.FromSeconds(10));
            _producer.Dispose();
            _producer = null;
        }

        _adminClient?.Dispose();
        _adminClient = null;
        _ensuredTopics.Clear();

        _status = TransportStatus.Disconnected;
        LogDisconnected();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
    {
        EnsureConnected();
        await EnsureTopicAsync(topic).ConfigureAwait(false);

        var message = new Message<string, byte[]>
        {
            // Partition strategy: explicit [PartitionKey] header wins (per-key ordering),
            // then CorrelationId (per-correlation ordering), then MessageId (no ordering intent).
            Key = GetPartitionKey(context),
            Value = payload.ToArray(),
            Headers = BuildHeaders(context),
        };

        await _producer!.ProduceAsync(topic, message, ct).ConfigureAwait(false);
        LogPublished(topic, context.MessageId);
    }

    /// <inheritdoc />
    public Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
        => PublishAsync(payload, queue, context, ct);

    /// <inheritdoc />
    public Task<IAsyncDisposable> SubscribeAsync(
        string topic,
        string subscriptionName,
        Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        EnsureConnected();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = options.GroupId ?? subscriptionName,
            EnableAutoCommit = options.EnableAutoCommit,
            AutoOffsetReset = Enum.Parse<AutoOffsetReset>(options.AutoOffsetReset, true),
            MaxPollIntervalMs = options.MaxPollIntervalMs,
            SessionTimeoutMs = options.SessionTimeoutMs,
        };

        // Create the topic before the group subscribes: joining a non-existent topic parks the
        // consumer in a metadata-refresh loop until someone publishes.
        EnsureTopicAsync(topic).GetAwaiter().GetResult();

        var consumer = new ConsumerBuilder<string, byte[]>(consumerConfig).Build();
        consumer.Subscribe(topic);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Register the consumer BEFORE starting the background loop so that a concurrent
        // DisconnectAsync cannot miss it and leave an orphaned loop running.
        _consumers.TryAdd(subscriptionName, (consumer, cts));

        // Start consumer loop on background thread
        _ = Task.Run(async () =>
        {
            LogConsumerStarted(subscriptionName, topic);
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    ConsumeResult<string, byte[]>? result = null;
                    try
                    {
                        result = consumer.Consume(cts.Token);
                        if (result?.Message?.Value is null) continue;

                        var context = ExtractContext(result.Message);
                        await handler(result.Message.Value, context, cts.Token).ConfigureAwait(false);

                        if (!options.EnableAutoCommit)
                            consumer.Commit(result);
                    }
                    catch (ConsumeException ex)
                    {
                        LogConsumeError(subscriptionName, ex);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogHandlerError(subscriptionName, ex);
                        await HandleFailedMessageAsync(consumer, result, topic, subscriptionName, ex, cts.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { /* consumer disposed by DisconnectAsync */ }
            finally
            {
                LogConsumerStopped(subscriptionName, topic);
            }
        }, cts.Token);

        // The subscription handle only cancels the CTS; actual cleanup is done by DisconnectAsync.
        IAsyncDisposable disposable = new KafkaSubscription(subscriptionName, this);
        return Task.FromResult(disposable);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);

    /// <summary>
    ///     Dead-letters a message whose handler threw. Without this, the failed offset is skipped
    ///     in-session and gets implicitly committed by the next successful message on the same
    ///     partition — silent data loss. Publishes the raw message to <c>"{topic}{suffix}"</c> with
    ///     the original headers plus failure metadata, then commits the failed offset so it is
    ///     accounted. If the dead-letter publish itself fails, the offset is NOT committed (the
    ///     message is redelivered on the next rebalance/restart).
    /// </summary>
    private async Task HandleFailedMessageAsync(
        IConsumer<string, byte[]> consumer,
        ConsumeResult<string, byte[]>? result,
        string topic,
        string subscriptionName,
        Exception error,
        CancellationToken ct)
    {
        if (result?.Message is null) return;

        if (!options.EnableDeadLetter)
        {
            LogDeadLetterDisabled(subscriptionName, topic);
            return;
        }

        var deadLetterTopic = topic + options.DeadLetterTopicSuffix;
        try
        {
            await EnsureTopicAsync(deadLetterTopic).ConfigureAwait(false);
            var headers = result.Message.Headers ?? new Headers();
            headers.Add("X-Pragmatic-DeadLetter-Source", Encoding.UTF8.GetBytes(topic));
            headers.Add("X-Pragmatic-DeadLetter-Reason", Encoding.UTF8.GetBytes(error.Message));
            headers.Add("X-Pragmatic-DeadLetter-Subscription", Encoding.UTF8.GetBytes(subscriptionName));

            await _producer!.ProduceAsync(deadLetterTopic, new Message<string, byte[]>
            {
                Key = result.Message.Key,
                Value = result.Message.Value,
                Headers = headers,
            }, ct).ConfigureAwait(false);

            if (!options.EnableAutoCommit)
                consumer.Commit(result);

            MessagingDiagnostics.DeadLettered.Add(1);
            LogDeadLettered(subscriptionName, deadLetterTopic);
        }
        catch (Exception dlqEx) when (dlqEx is not OperationCanceledException)
        {
            // Do NOT commit: the message will be redelivered on rebalance/restart.
            LogDeadLetterFailed(subscriptionName, deadLetterTopic, dlqEx);
        }
    }

    private const string PragmaticHeaderPrefix = "X-Pragmatic-";

    /// <summary>Explicit [PartitionKey] header → CorrelationId → MessageId.</summary>
    private static string GetPartitionKey(MessageContext context)
        => context.Headers is not null
            && context.Headers.TryGetValue(IPartitionKeyResolver.HeaderName, out var key)
            && !string.IsNullOrEmpty(key)
                ? key
                : context.CorrelationId ?? context.MessageId;

    private static Headers BuildHeaders(MessageContext context)
    {
        var headers = new Headers
        {
            { "X-Pragmatic-MessageId", Encoding.UTF8.GetBytes(context.MessageId) }
        };

        if (context.TenantId is not null)
            headers.Add("X-Pragmatic-TenantId", Encoding.UTF8.GetBytes(context.TenantId));
        if (context.UserId is not null)
            headers.Add("X-Pragmatic-UserId", Encoding.UTF8.GetBytes(context.UserId));
        if (context.CorrelationId is not null)
            headers.Add("X-Pragmatic-CorrelationId", Encoding.UTF8.GetBytes(context.CorrelationId));

        // Propagate custom application headers. Reserved Pragmatic keys win.
        if (context.Headers is not null)
        {
            foreach (var kv in context.Headers)
            {
                if (kv.Key.StartsWith(PragmaticHeaderPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                headers.Add(kv.Key, Encoding.UTF8.GetBytes(kv.Value ?? ""));
            }
        }

        return headers;
    }

    private static MessageContext ExtractContext(Message<string, byte[]> message)
    {
        return new MessageContext(
            MessageId: GetHeader(message.Headers, "X-Pragmatic-MessageId") ?? Guid.NewGuid().ToString("N"),
            CorrelationId: GetHeader(message.Headers, "X-Pragmatic-CorrelationId") ?? message.Key,
            TenantId: GetHeader(message.Headers, "X-Pragmatic-TenantId"),
            UserId: GetHeader(message.Headers, "X-Pragmatic-UserId"),
            Headers: ExtractCustomHeaders(message.Headers));
    }

    private static IReadOnlyDictionary<string, string>? ExtractCustomHeaders(Headers? headers)
    {
        if (headers is null || headers.Count == 0) return null;
        Dictionary<string, string>? custom = null;
        foreach (var h in headers)
        {
            if (h.Key.StartsWith(PragmaticHeaderPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var bytes = h.GetValueBytes();
            custom ??= new Dictionary<string, string>(StringComparer.Ordinal);
            custom[h.Key] = bytes is not null ? Encoding.UTF8.GetString(bytes) : "";
        }
        return custom;
    }

    private static string? GetHeader(Headers? headers, string key)
    {
        if (headers is null) return null;
        try
        {
            var header = headers.GetLastBytes(key);
            return header is not null ? Encoding.UTF8.GetString(header) : null;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureConnected()
    {
        if (_status != TransportStatus.Connected || _producer is null)
            throw new InvalidOperationException("Kafka transport is not connected. Call ConnectAsync first.");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to Kafka: {BootstrapServers}")]
    private partial void LogConnected(string bootstrapServers);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disconnected from Kafka")]
    private partial void LogDisconnected();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Published to Kafka topic {Topic} (id: {MessageId})")]
    private partial void LogPublished(string topic, string messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Consumer {Subscription} started on topic {Topic}")]
    private partial void LogConsumerStarted(string subscription, string topic);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Consumer {Subscription} stopped on topic {Topic}")]
    private partial void LogConsumerStopped(string subscription, string topic);

    [LoggerMessage(Level = LogLevel.Error, Message = "Consumer {Subscription} consume error")]
    private partial void LogConsumeError(string subscription, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Consumer {Subscription} handler error")]
    private partial void LogHandlerError(string subscription, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consumer {Subscription} dead-lettered a message to {DeadLetterTopic}")]
    private partial void LogDeadLettered(string subscription, string deadLetterTopic);

    [LoggerMessage(Level = LogLevel.Error, Message = "Consumer {Subscription} FAILED to dead-letter to {DeadLetterTopic}; offset not committed, message will be redelivered on rebalance/restart")]
    private partial void LogDeadLetterFailed(string subscription, string deadLetterTopic, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consumer {Subscription} on {Topic}: dead-letter DISABLED — the failed message's offset will be implicitly committed by the next successful message and the message LOST. Enable KafkaOptions.EnableDeadLetter for production.")]
    private partial void LogDeadLetterDisabled(string subscription, string topic);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created Kafka topic {Topic} ({Partitions} partitions)")]
    private partial void LogTopicCreated(string topic, int partitions);

    private sealed class KafkaSubscription(string subscriptionName, KafkaTransport transport) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            // Cancel only this subscription's CTS; the consumer/CTS are owned by the transport
            // and will be fully disposed by DisconnectAsync.
            if (transport._consumers.TryGetValue(subscriptionName, out var entry))
                entry.Cts.Cancel();
            return ValueTask.CompletedTask;
        }
    }
}
