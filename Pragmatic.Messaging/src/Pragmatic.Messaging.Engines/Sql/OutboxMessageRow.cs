using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Messaging.Engines.Sql;

/// <summary>
///     A message in the transactional outbox of a boundary the Sql engine serves (<c>__OutboxMessages</c>), written in the save that raised its
///     event (<see cref="SqlMessagingOutboxPhase" />) and read by the delivery pump through <see cref="SqlOutboxSource" />. The table is
///     EF Core's (<c>OutboxEntityTypeConfiguration</c>), column for column, its instants UTC ticks: a database holds one <c>__OutboxMessages</c>
///     whichever engine writes it.
/// </summary>
[Entity]
[Table("__OutboxMessages")]
public sealed class OutboxMessageRow : IOutboxMessageRecord
{
    public Guid Id { get; set; }

    /// <summary>The event's full type name, which the pump resolves through the generated message type registries.</summary>
    [MaxLength(512)]
    public string MessageType { get; set; } = "";

    /// <summary>The event, serialized through the generated JSON context.</summary>
    public string Payload { get; set; } = "";

    /// <summary>When the row was written, in UTC ticks: the delivery's order.</summary>
    public long CreatedAt { get; set; }

    /// <summary>When the message was published (or dead-lettered), in UTC ticks; null while pending.</summary>
    public long? ProcessedAt { get; set; }

    public int RetryCount { get; set; }

    /// <summary>The earliest instant, in UTC ticks, of the next attempt after a failure; null when eligible at once.</summary>
    public long? NextAttemptAt { get; set; }

    [MaxLength(2048)]
    public string? Error { get; set; }

    /// <summary>The poll holding the row; null when unclaimed.</summary>
    [MaxLength(128)]
    public string? ClaimedBy { get; set; }

    /// <summary>Until when, in UTC ticks, the claim holds: past it another poll takes the row (a crashed worker's).</summary>
    public long? ClaimedUntil { get; set; }

    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    /// <summary>The tenant of the save that raised the event, restored around its publish; null without one.</summary>
    [MaxLength(128)]
    public string? TenantId { get; set; }

    [MaxLength(128)]
    public string? UserId { get; set; }

    public string? HeadersJson { get; set; }
}
