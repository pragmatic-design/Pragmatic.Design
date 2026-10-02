using Microsoft.Extensions.Logging;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Messaging.Entities;

namespace Pragmatic.Messaging.Jobs;

/// <summary>
///     Job that deserializes and publishes a scheduled message via <see cref="IMessageBus"/>.
///     Uses <see cref="IMessageTypeRegistry"/> for AOT-safe deserialization.
/// </summary>
/// <remarks>
///     ⚠️ <c>[Job]</c> because it <b>is</b> one, and saying so is what makes it runnable: the generator
///     emits the registry that knows a job type, and this one had none — so
///     <c>EnableScheduledMessages()</c> registered a scheduler whose work the runner then refused as
///     "Unknown job type", after the original message had been acknowledged.
/// </remarks>
[Job]
public sealed partial class PublishMessageJob(
    IMessageBus messageBus,
    IEnumerable<IMessageTypeRegistry> messageTypeRegistries,
    ILogger<PublishMessageJob> logger,
    IDeadLetterStore? deadLetterStore = null,
    IIdempotencyStore? idempotencyStore = null) : IJob<PublishMessageParams>
{
    public async Task ExecuteAsync(PublishMessageParams parameters, JobContext context, CancellationToken ct)
    {
        LogDeserializing(parameters.MessageTypeName, context.JobId);

        // Preserve the original MessageId when present (redelivery / stable schedule-time id): sibling
        // handlers that already succeeded dedupe on it. The JobId fallback covers params serialized
        // before schedule-time ids existed.
        var messageContext = new MessageContext(
            MessageId: parameters.MessageId ?? context.JobId.ToString("N"),
            CorrelationId: parameters.CorrelationId ?? context.CorrelationId,
            TenantId: parameters.TenantId ?? context.TenantId,
            UserId: parameters.UserId,
            RetryCount: parameters.RetryCount,
            SourceBoundary: parameters.SourceBoundary);

        // One registry per module assembly (SG-generated): try each until one knows the type.
        object? message = null;
        foreach (var registry in messageTypeRegistries)
        {
            message = registry.Deserialize(parameters.MessageTypeName, parameters.SerializedMessage);
            if (message is not null) break;
        }
        if (message is null)
        {
            LogUnknownType(parameters.MessageTypeName, context.JobId);

            // An unknown type never becomes known by retrying — throwing would poison the JOB store, where
            // ops don't look. Route it to the dead-letter store instead (dashboard-visible / replayable) and
            // let the job succeed. Fall back to failing the job only when no dead-letter store is registered.
            if (deadLetterStore is not null)
            {
                await deadLetterStore.StoreAsync(new DeadLetterMessage(
                    MessageType: parameters.MessageTypeName,
                    Payload: parameters.SerializedMessage,
                    Error: $"Unknown message type '{parameters.MessageTypeName}' — no IMessageTypeRegistry could deserialize it.",
                    RetryCount: parameters.RetryCount,
                    Context: messageContext,
                    FailedAt: DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
                return;
            }

            throw new InvalidOperationException(
                $"Cannot deserialize scheduled message: unknown type '{parameters.MessageTypeName}'. " +
                "Ensure IMessageTypeRegistry is registered (SG-generated), or register an IDeadLetterStore " +
                "so undeserializable scheduled messages are dead-lettered instead of poisoning the job store.");
        }

        // ⚠️ This id is about to be delivered again, so the consume side must stop calling it handled.
        //
        // The bus claims the bare MessageId before dispatching and marks the claim COMPLETED when the
        // dispatch returns. A handler carrying [Redelivery] does not throw — it releases its own
        // per-handler claim, schedules this job and returns — so the dispatch looks successful, the id
        // is completed, and the copy this job republishes (the SAME MessageId, deliberately, so sibling
        // handlers still dedupe) would be dropped at the bus before any handler saw it: the message
        // crosses the broker twice, the second copy never reaches the handler, and the message is
        // neither retried nor dead-lettered.
        //
        // Released here rather than where the redelivery is scheduled, because there the bus completes
        // the claim immediately afterwards. Idempotent, and a no-op for an ordinary scheduled message:
        // that one carries an id minted at schedule time, which no consumer has claimed.
        //
        // ⚠️ The per-handler claims are untouched, which is the point: a sibling that already succeeded
        // holds `{messageId}:{handler}` and still skips this delivery.
        if (idempotencyStore is not null)
            await idempotencyStore.RemoveAsync(messageContext.MessageId, ct).ConfigureAwait(false);

        // Publish through the active transport so scheduled cross-service
        // messages actually leave the process. DispatchAsync is local-only.
        await messageBus.PublishAsync(message, message.GetType(), messageContext, ct).ConfigureAwait(false);

        LogPublished(parameters.MessageTypeName, context.JobId);
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Deserializing scheduled message {MessageType} (job: {JobId})")]
    private partial void LogDeserializing(string messageType, Guid jobId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Unknown message type {MessageType} in scheduled job {JobId}")]
    private partial void LogUnknownType(string messageType, Guid jobId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Published scheduled message {MessageType} (job: {JobId})")]
    private partial void LogPublished(string messageType, Guid jobId);
}
