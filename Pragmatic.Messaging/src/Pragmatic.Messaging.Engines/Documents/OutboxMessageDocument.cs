using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Messaging.Engines.Documents;

/// <summary>
///     A message in the transactional outbox of a boundary the documents engine serves (collection <c>__OutboxMessages</c>), written in the save
///     that raised its event (<see cref="DocMessagingOutboxPhase" />) and read by the delivery pump through <see cref="DocOutboxSource" />. The
///     fields are <see cref="Sql.OutboxMessageRow" />'s columns, its instants UTC ticks.
/// </summary>
[Document("__OutboxMessages")]
public sealed partial class OutboxMessageDocument : IOutboxMessageRecord
{
    /// <summary>
    ///     The collection in a boundary's <c>{Boundary}DocSchema</c>: the index the claim reads (pending, past its backoff, re-claimable, oldest
    ///     first) and the correlation lookup, named as EF Core's on <c>__OutboxMessages</c>.
    /// </summary>
    public static readonly DocCollectionSchema Schema = new("__OutboxMessages", "/id",
    [
        new DocIndex("IX___OutboxMessages_ProcessedAt_NextAttemptAt_ClaimedUntil_CreatedAt", ["ProcessedAt", "NextAttemptAt", "ClaimedUntil", "CreatedAt"]),
        new DocIndex("IX___OutboxMessages_CorrelationId", ["CorrelationId"]),
    ], []);

    [Key]
    public Guid Id { get; set; }

    /// <summary>The event's full type name, which the pump resolves through the generated message type registries.</summary>
    public string MessageType { get; set; } = "";

    /// <summary>The event, serialized through the generated JSON context.</summary>
    public string Payload { get; set; } = "";

    /// <summary>When the document was written, in UTC ticks: the delivery's order.</summary>
    public long CreatedAt { get; set; }

    /// <summary>When the message was published (or dead-lettered), in UTC ticks; null while pending.</summary>
    public long? ProcessedAt { get; set; }

    public int RetryCount { get; set; }

    /// <summary>The earliest instant, in UTC ticks, of the next attempt after a failure; null when eligible at once.</summary>
    public long? NextAttemptAt { get; set; }

    public string? Error { get; set; }

    /// <summary>The poll holding the document; null when unclaimed.</summary>
    public string? ClaimedBy { get; set; }

    /// <summary>Until when, in UTC ticks, the claim holds: past it another poll takes the document (a crashed worker's).</summary>
    public long? ClaimedUntil { get; set; }

    public string? CorrelationId { get; set; }

    /// <summary>The tenant of the save that raised the event, restored around its publish; null without one.</summary>
    public string? TenantId { get; set; }

    public string? UserId { get; set; }

    public string? HeadersJson { get; set; }
}
