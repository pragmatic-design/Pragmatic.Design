using Pragmatic.Messaging.Entities;

namespace Pragmatic.Messaging.Engines;

/// <summary>Between an <see cref="OutboxMessage" /> and the row or document that stores it: instants in UTC ticks.</summary>
internal static class OutboxMessageRecords
{
    /// <summary><paramref name="record" />, filled from a captured <paramref name="message" />: pending, unclaimed.</summary>
    public static T From<T>(T record, OutboxMessage message) where T : IOutboxMessageRecord
    {
        record.Id = message.Id;
        record.MessageType = message.MessageType;
        record.Payload = message.Payload;
        record.CreatedAt = message.CreatedAt.UtcTicks;
        record.RetryCount = message.RetryCount;
        record.CorrelationId = message.CorrelationId;
        record.TenantId = message.TenantId;
        record.UserId = message.UserId;
        record.HeadersJson = message.HeadersJson;
        return record;
    }

    /// <summary>The message <paramref name="record" /> stores, as the delivery pump reads it.</summary>
    public static OutboxMessage ToMessage(IOutboxMessageRecord record) => new()
    {
        Id = record.Id,
        MessageType = record.MessageType,
        Payload = record.Payload,
        CreatedAt = Instant(record.CreatedAt),
        ProcessedAt = Instant(record.ProcessedAt),
        RetryCount = record.RetryCount,
        NextAttemptAt = Instant(record.NextAttemptAt),
        Error = record.Error,
        ClaimedBy = record.ClaimedBy,
        ClaimedUntil = Instant(record.ClaimedUntil),
        CorrelationId = record.CorrelationId,
        TenantId = record.TenantId,
        UserId = record.UserId,
        HeadersJson = record.HeadersJson,
    };

    public static DateTimeOffset Instant(long ticks) => new(ticks, TimeSpan.Zero);

    public static DateTimeOffset? Instant(long? ticks) => ticks is { } t ? Instant(t) : null;
}
