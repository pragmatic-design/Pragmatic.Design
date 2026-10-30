namespace Pragmatic.Messaging.Engines;

/// <summary>
///     An outbox message as both engines store it: <see cref="Entities.OutboxMessage" />'s fields, its instants UTC ticks (the ordering and
///     range comparisons of the claim are then numeric on every database, as EF Core's ticks converter makes them).
/// </summary>
internal interface IOutboxMessageRecord
{
    Guid Id { get; set; }

    string MessageType { get; set; }

    string Payload { get; set; }

    long CreatedAt { get; set; }

    long? ProcessedAt { get; set; }

    int RetryCount { get; set; }

    long? NextAttemptAt { get; set; }

    string? Error { get; set; }

    string? ClaimedBy { get; set; }

    long? ClaimedUntil { get; set; }

    string? CorrelationId { get; set; }

    string? TenantId { get; set; }

    string? UserId { get; set; }

    string? HeadersJson { get; set; }
}
