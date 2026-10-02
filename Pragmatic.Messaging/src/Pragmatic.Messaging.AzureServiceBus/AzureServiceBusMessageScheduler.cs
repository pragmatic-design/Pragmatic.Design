using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.AzureServiceBus;

/// <summary>
///     <see cref="IMessageScheduler"/> backed by Azure Service Bus NATIVE scheduled enqueue —
///     no database polling: the broker holds the message and releases it at the scheduled time.
/// </summary>
/// <remarks>
///     Cancellation needs the broker sequence number. With an <see cref="IScheduleHandleStore"/>
///     registered (e.g. <c>EnableEfCorePersistence()</c>) handles are durable and cancel is
///     restart-safe; without one they live in an in-process map — delivery itself is always
///     durable on the broker, but ids from before a restart can no longer be cancelled.
/// </remarks>
public sealed partial class AzureServiceBusMessageScheduler(
    AzureServiceBusTransport transport,
    IMessageRouter router,
    IMessageSerializer serializer,
    ILogger<AzureServiceBusMessageScheduler> logger,
    IScheduleHandleStore? handleStore = null) : IMessageScheduler
{
    // Fast path and fallback: always populated; the durable store survives restarts.
    private readonly ConcurrentDictionary<Guid, (string Topic, long SequenceNumber)> _schedules = new();

    /// <inheritdoc />
    public Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, CancellationToken ct = default)
        where T : notnull
        => ScheduleAsync(message, DateTimeOffset.UtcNow + delay, ct);

    /// <inheritdoc />
    public async Task<Guid> ScheduleAsync<T>(T message, DateTimeOffset scheduledAt, CancellationToken ct = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        return await ScheduleCoreAsync(message, scheduledAt, MessageContext.New(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, MessageContext context, CancellationToken ct = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        return await ScheduleCoreAsync(message, DateTimeOffset.UtcNow + delay, context, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CancelAsync(Guid scheduleId, CancellationToken ct = default)
    {
        var entry = _schedules.TryRemove(scheduleId, out var local)
            ? local
            : await ResolveFromStoreAsync(scheduleId, ct).ConfigureAwait(false);

        if (entry is not { } handle)
        {
            LogCancelUnknown(scheduleId);
            return;
        }

        var client = transport.Client
            ?? throw new InvalidOperationException("Azure Service Bus transport is not connected.");
        var sender = client.CreateSender(handle.Topic);
        await using (sender.ConfigureAwait(false))
        {
            await sender.CancelScheduledMessageAsync(handle.SequenceNumber, ct).ConfigureAwait(false);
        }

        if (handleStore is not null)
            await handleStore.RemoveAsync(scheduleId, ct).ConfigureAwait(false);

        LogCancelled(scheduleId, handle.Topic);
    }

    private async Task<(string Topic, long SequenceNumber)?> ResolveFromStoreAsync(Guid scheduleId, CancellationToken ct)
    {
        if (handleStore is null)
            return null;

        var handle = await handleStore.GetAsync(scheduleId, ct).ConfigureAwait(false);
        return handle is null ? null : (handle.Topic, handle.SequenceNumber);
    }

    private async Task<Guid> ScheduleCoreAsync<T>(T message, DateTimeOffset scheduledAt, MessageContext context, CancellationToken ct)
        where T : notnull
    {
        var client = transport.Client
            ?? throw new InvalidOperationException("Azure Service Bus transport is not connected. Call ConnectAsync first.");

        var topic = router.GetTopic<T>();
        var payload = serializer.Serialize(message, typeof(T));

        var sbMessage = new Azure.Messaging.ServiceBus.ServiceBusMessage(BinaryData.FromBytes(payload))
        {
            MessageId = context.MessageId,
            CorrelationId = context.CorrelationId,
            ContentType = "application/json",
        };
        if (context.TenantId is not null)
            sbMessage.ApplicationProperties["X-Pragmatic-TenantId"] = context.TenantId;
        if (context.UserId is not null)
            sbMessage.ApplicationProperties["X-Pragmatic-UserId"] = context.UserId;
        if (context.RetryCount > 0)
            sbMessage.ApplicationProperties["X-Pragmatic-RetryCount"] = context.RetryCount;

        long sequenceNumber;
        var sender = client.CreateSender(topic);
        await using (sender.ConfigureAwait(false))
        {
            sequenceNumber = await sender.ScheduleMessageAsync(sbMessage, scheduledAt, ct).ConfigureAwait(false);
        }

        var scheduleId = Guid.NewGuid();
        _schedules.TryAdd(scheduleId, (topic, sequenceNumber));
        if (handleStore is not null)
            await handleStore.SaveAsync(new ScheduleHandle(scheduleId, topic, sequenceNumber), ct).ConfigureAwait(false);

        LogScheduled(typeof(T).Name, topic, scheduledAt);
        return scheduleId;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Scheduled {MessageType} on {Topic} for {ScheduledAt} (broker-native)")]
    private partial void LogScheduled(string messageType, string topic, DateTimeOffset scheduledAt);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cancelled scheduled message {ScheduleId} on {Topic}")]
    private partial void LogCancelled(Guid scheduleId, string topic);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cancel requested for unknown schedule {ScheduleId} (not from this process and no IScheduleHandleStore?)")]
    private partial void LogCancelUnknown(Guid scheduleId);
}
