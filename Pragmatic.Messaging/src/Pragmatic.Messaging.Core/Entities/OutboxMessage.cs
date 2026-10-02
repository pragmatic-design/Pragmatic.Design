namespace Pragmatic.Messaging.Entities;

/// <summary>
///     Outbox message entity persisted alongside business data in the same transaction.
///     The outbox delivery service polls and delivers these messages.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Fully qualified type name of the message (e.g., "Showcase.Booking.Events.ReservationCreatedEvent").</summary>
    public required string MessageType { get; set; }

    /// <summary>Serialized message payload as JSON.</summary>
    public required string Payload { get; set; }

    /// <summary>When the message was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the message was successfully processed. Null if pending.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Number of delivery attempts so far.</summary>
    public int RetryCount { get; set; }

    /// <summary>
    ///     Earliest time the message becomes eligible for (re)delivery. Null = eligible immediately.
    ///     Set to an exponential-backoff instant on each failure so a failing message is not
    ///     re-selected on every poll.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>Last error message if delivery failed.</summary>
    public string? Error { get; set; }

    /// <summary>
    ///     Identifier of the worker that has claimed this row for delivery. Null when unclaimed.
    ///     Set atomically by the delivery query so only one replica processes a given message
    ///     (prevents multi-instance double-delivery).
    /// </summary>
    public string? ClaimedBy { get; set; }

    /// <summary>
    ///     Instant until which the current claim is held. A row whose claim has expired
    ///     (<c>ClaimedUntil &lt; now</c>) is eligible to be re-claimed, so a crashed worker's
    ///     messages are not stuck forever. Null when unclaimed.
    /// </summary>
    public DateTimeOffset? ClaimedUntil { get; set; }

    /// <summary>Correlation identifier for tracing.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Tenant identifier for multi-tenant scenarios.</summary>
    public string? TenantId { get; set; }

    /// <summary>User identifier who originated the message.</summary>
    public string? UserId { get; set; }

    /// <summary>Serialized headers as JSON.</summary>
    public string? HeadersJson { get; set; }
}
