using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Messaging.AzureServiceBus;

/// <summary>
///     Azure Service Bus implementation of <see cref="IMessageTransport"/>.
///     Publish = topic (fan-out via subscriptions); Send = queue (point-to-point).
///     Failed handlers abandon the message: ASB redelivers up to MaxDeliveryCount and then
///     dead-letters NATIVELY — no silent loss by construction.
/// </summary>
public sealed partial class AzureServiceBusTransport(
    AzureServiceBusOptions options,
    ILogger<AzureServiceBusTransport> logger) : IMessageTransport, IQueueConsumerTransport
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();
    private readonly ConcurrentDictionary<string, ServiceBusProcessor> _processors = new();

    private ServiceBusClient? _client;
    private volatile TransportStatus _status = TransportStatus.Disconnected;
    private int _disposed;

    /// <inheritdoc />
    public string Name => "AzureServiceBus";

    /// <inheritdoc />
    public TransportStatus Status => _status;

    /// <summary>The underlying client (senders/schedulers share it). Null until connected.</summary>
    internal ServiceBusClient? Client => _client;

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken ct = default)
    {
        _status = TransportStatus.Connecting;
        try
        {
            _client = new ServiceBusClient(options.ConnectionString);
            InitializeAdministration();
            _status = TransportStatus.Connected;
            LogConnected();
        }
        catch (Exception ex)
        {
            _status = TransportStatus.Faulted;
            LogConnectionFailed(ex);
            throw;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        foreach (var processor in _processors.Values)
        {
            try
            {
                await processor.StopProcessingAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // best-effort shutdown
            }

            await processor.DisposeAsync().ConfigureAwait(false);
        }

        _processors.Clear();

        foreach (var sender in _senders.Values)
            await sender.DisposeAsync().ConfigureAwait(false);
        _senders.Clear();

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }

        _status = TransportStatus.Disconnected;
        LogDisconnected();
    }

    /// <inheritdoc />
    public async Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
    {
        EnsureConnected();
        await EnsureTopicAsync(topic, ct).ConfigureAwait(false);

        var sender = GetSender(topic);
        await sender.SendMessageAsync(BuildMessage(payload, context), ct).ConfigureAwait(false);
        LogPublished(topic, context.MessageId);
    }

    /// <inheritdoc />
    public async Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
    {
        EnsureConnected();
        await EnsureQueueAsync(queue, ct).ConfigureAwait(false);

        var sender = GetSender(queue);
        await sender.SendMessageAsync(BuildMessage(payload, context), ct).ConfigureAwait(false);
        LogSent(queue, context.MessageId);
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable> SubscribeAsync(
        string topic,
        string subscriptionName,
        Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        EnsureConnected();
        await EnsureTopicAsync(topic, ct).ConfigureAwait(false);
        await EnsureSubscriptionAsync(topic, subscriptionName, ct).ConfigureAwait(false);

        var processor = _client!.CreateProcessor(topic, subscriptionName, ProcessorOptions());
        return await StartProcessorAsync(processor, $"{topic}/{subscriptionName}", subscriptionName, topic, handler, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     ASB separates queues from topics: point-to-point consumption (request/reply queues)
    ///     must use a QUEUE processor — <see cref="SubscribeAsync"/> would create a topic
    ///     subscription that never sees <see cref="SendAsync"/> traffic.
    /// </remarks>
    public async Task<IAsyncDisposable> SubscribeQueueAsync(
        string queue,
        Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        EnsureConnected();
        await EnsureQueueAsync(queue, ct).ConfigureAwait(false);

        var processor = _client!.CreateProcessor(queue, ProcessorOptions());
        return await StartProcessorAsync(processor, $"q/{queue}", queue, queue, handler, ct).ConfigureAwait(false);
    }

    private ServiceBusProcessorOptions ProcessorOptions() => new()
    {
        AutoCompleteMessages = false,
        MaxConcurrentCalls = options.MaxConcurrentCalls,
        PrefetchCount = options.PrefetchCount,
    };

    private async Task<IAsyncDisposable> StartProcessorAsync(
        ServiceBusProcessor processor,
        string registryKey,
        string subscriptionName,
        string source,
        Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
        CancellationToken ct)
    {
        processor.ProcessMessageAsync += async args =>
        {
            var context = ExtractContext(args.Message);
            try
            {
                // CancellationToken.None for in-flight handlers: the processor token fires on
                // shutdown and would interrupt processing already in progress.
                await handler(args.Message.Body.ToMemory(), context, CancellationToken.None).ConfigureAwait(false);
                await args.CompleteMessageAsync(args.Message, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogConsumerError(subscriptionName, ex);
                // Abandon → broker redelivery; after MaxDeliveryCount ASB dead-letters natively.
                await args.AbandonMessageAsync(args.Message, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
        };

        processor.ProcessErrorAsync += args =>
        {
            LogProcessorError(subscriptionName, args.ErrorSource.ToString(), args.Exception);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(ct).ConfigureAwait(false);
        _processors.TryAdd(registryKey, processor);
        LogSubscribed(subscriptionName, source);

        return new ServiceBusSubscription(processor);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await DisconnectAsync().ConfigureAwait(false);
    }

    private ServiceBusSender GetSender(string entity)
        => _senders.GetOrAdd(entity, e => _client!.CreateSender(e));

    private void EnsureConnected()
    {
        if (_status != TransportStatus.Connected || _client is null)
            throw new InvalidOperationException("Azure Service Bus transport is not connected. Call ConnectAsync first.");
    }

    private static ServiceBusMessage BuildMessage(ReadOnlyMemory<byte> payload, MessageContext context)
    {
        var message = new ServiceBusMessage(BinaryData.FromBytes(payload))
        {
            MessageId = context.MessageId,
            CorrelationId = context.CorrelationId,
            ContentType = "application/json",
        };

        // Sessions/partitioned entities honor the partition key when present.
        if (context.Headers is not null &&
            context.Headers.TryGetValue(IPartitionKeyResolver.HeaderName, out var partitionKey) &&
            !string.IsNullOrEmpty(partitionKey))
        {
            message.PartitionKey = partitionKey;
        }

        FillApplicationProperties(message, context);
        return message;
    }

    // =========================================================================
    // LoggerMessage
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to Azure Service Bus")]
    private partial void LogConnected();

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to connect to Azure Service Bus")]
    private partial void LogConnectionFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disconnected from Azure Service Bus")]
    private partial void LogDisconnected();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Published to topic {Topic} (id: {MessageId})")]
    private partial void LogPublished(string topic, string messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sent to queue {Queue} (id: {MessageId})")]
    private partial void LogSent(string queue, string messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Subscribed {Subscription} to {Topic}")]
    private partial void LogSubscribed(string subscription, string topic);

    [LoggerMessage(Level = LogLevel.Error, Message = "Consumer {Subscription} handler error (message abandoned for redelivery/dead-letter)")]
    private partial void LogConsumerError(string subscription, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processor error on {Subscription} (source: {ErrorSource})")]
    private partial void LogProcessorError(string subscription, string errorSource, Exception ex);

    // =========================================================================
    // Subscription handle
    // =========================================================================

    private sealed class ServiceBusSubscription(ServiceBusProcessor processor) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await processor.StopProcessingAsync().ConfigureAwait(false);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
