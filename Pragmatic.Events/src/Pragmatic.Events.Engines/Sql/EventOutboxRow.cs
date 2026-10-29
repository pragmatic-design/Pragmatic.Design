using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Events.Engines.Sql;

/// <summary>
///     A domain event in the transactional outbox of a boundary the Sql engine serves (<c>__EventOutbox</c>), written in the save that raised it
///     (<see cref="SqlEventOutboxPhase" />) and delivered by <see cref="SqlEventOutboxDrainer{TBoundary}" />. The table is EF Core's
///     <c>EventOutboxEntry</c>, column for column, its instants UTC ticks: a database holds one <c>__EventOutbox</c> whichever engine writes it.
/// </summary>
[Entity]
[Table("__EventOutbox")]
public sealed class EventOutboxRow : IEventOutboxRecord
{
    public Guid Id { get; set; }

    /// <summary>The event's assembly-qualified type name, which the delivery resolves through the allowlist.</summary>
    [MaxLength(512)]
    public string EventType { get; set; } = "";

    /// <summary>The event, serialized through the generated JSON context.</summary>
    public string Payload { get; set; } = "";

    /// <summary>When the event occurred, in UTC ticks.</summary>
    public long OccurredAt { get; set; }

    /// <summary>The tenant of the save that raised the event, restored around its delivery; null without one.</summary>
    [MaxLength(128)]
    public string? TenantId { get; set; }

    /// <summary>The W3C trace the event was raised in, which its delivery joins; null without one.</summary>
    [MaxLength(64)]
    public string? TraceParent { get; set; }

    /// <summary>When the row was written, in UTC ticks: the delivery's order.</summary>
    public long CreatedAt { get; set; }

    /// <summary>When the event was delivered, in UTC ticks; null while pending.</summary>
    public long? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    /// <summary>The delivery pass holding the row; null when unclaimed.</summary>
    [MaxLength(128)]
    public string? ClaimedBy { get; set; }

    /// <summary>Until when, in UTC ticks, the claim holds: past it another pass takes the row (a crashed worker's).</summary>
    public long? ClaimedUntil { get; set; }
}
