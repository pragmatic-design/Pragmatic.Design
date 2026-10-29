using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Events.Engines.Documents;

/// <summary>
///     A domain event in the transactional outbox of a boundary the documents engine serves (collection <c>__EventOutbox</c>), written in the save
///     that raised it (<see cref="DocEventOutboxPhase" />) and delivered by <see cref="DocEventOutboxDrainer{TBoundary}" />. The fields are
///     <see cref="Sql.EventOutboxRow" />'s columns, its instants UTC ticks.
/// </summary>
[Document("__EventOutbox")]
public sealed partial class EventOutboxDocument : IEventOutboxRecord
{
    /// <summary>
    ///     The collection in a boundary's <c>{Boundary}DocSchema</c>: the index the delivery's claim reads (pending, re-claimable, oldest first),
    ///     named as EF Core's on <c>__EventOutbox</c>.
    /// </summary>
    public static readonly DocCollectionSchema Schema = new("__EventOutbox", "/id",
        [new DocIndex("IX___EventOutbox_ProcessedAt_ClaimedUntil_CreatedAt", ["ProcessedAt", "ClaimedUntil", "CreatedAt"])], []);

    [Key]
    public Guid Id { get; set; }

    /// <summary>The event's assembly-qualified type name, which the delivery resolves through the allowlist.</summary>
    public string EventType { get; set; } = "";

    /// <summary>The event, serialized through the generated JSON context.</summary>
    public string Payload { get; set; } = "";

    /// <summary>When the event occurred, in UTC ticks.</summary>
    public long OccurredAt { get; set; }

    /// <summary>The tenant of the save that raised the event, restored around its delivery; null without one.</summary>
    public string? TenantId { get; set; }

    /// <summary>The W3C trace the event was raised in, which its delivery joins; null without one.</summary>
    public string? TraceParent { get; set; }

    /// <summary>When the document was written, in UTC ticks: the delivery's order.</summary>
    public long CreatedAt { get; set; }

    /// <summary>When the event was delivered, in UTC ticks; null while pending.</summary>
    public long? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    /// <summary>The delivery pass holding the document; null when unclaimed.</summary>
    public string? ClaimedBy { get; set; }

    /// <summary>Until when, in UTC ticks, the claim holds: past it another pass takes the document (a crashed worker's).</summary>
    public long? ClaimedUntil { get; set; }
}
