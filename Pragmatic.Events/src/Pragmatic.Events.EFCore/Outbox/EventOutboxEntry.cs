namespace Pragmatic.Events.EFCore.Outbox;

/// <summary>
///     A domain event persisted to the transactional outbox (<c>__EventOutbox</c> table).
///     Written in the same transaction as the entity changes that raised it, then delivered
///     asynchronously by <see cref="EventOutboxDeliveryService{TContext}"/>.
/// </summary>
public sealed class EventOutboxEntry
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Assembly-qualified type name of the domain event, used to deserialize the payload.</summary>
    public string EventType { get; set; } = default!;

    /// <summary>JSON-serialized domain event.</summary>
    public string Payload { get; set; } = default!;

    /// <summary>When the domain event occurred (from <c>IDomainEvent.OccurredAt</c>).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    ///     Tenant that owned the change which raised this event, captured from the ambient
    ///     <c>ITenantContext</c> at write time. Restored onto the delivery scope so tenant-scoped
    ///     queries inside event handlers run under the originating tenant. Null when no tenant
    ///     was resolved (single-tenant or system-context writes).
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    ///     W3C trace context (<c>Activity.Current?.Id</c>) captured when the event was written, so the
    ///     asynchronous delivery can re-attach to the originating distributed trace. Null when no
    ///     ambient <see cref="System.Diagnostics.Activity"/> was present at write time.
    /// </summary>
    public string? TraceParent { get; set; }

    /// <summary>When this outbox row was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the event was successfully delivered. Null while pending.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>
    ///     Number of delivery attempts made so far.
    ///     The delivery loop skips entries where <c>Attempts >= MaxAttempts</c> (see
    ///     <see cref="EventOutboxOptions.MaxAttempts" />), so entries exceeding the ceiling
    ///     are silently abandoned — inspect <see cref="LastError" /> for diagnostics.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>Error message from the most recent failed delivery attempt, if any.</summary>
    public string? LastError { get; set; }

    /// <summary>
    ///     Identifier of the worker that has claimed this entry for delivery. Null when unclaimed.
    ///     Stamped atomically by the delivery loop so only one replica delivers a given event
    ///     (prevents multi-instance double-delivery).
    /// </summary>
    public string? ClaimedBy { get; set; }

    /// <summary>
    ///     Instant until which the current claim is held. An entry whose claim has expired
    ///     (<c>ClaimedUntil &lt; now</c>) is re-claimable, so a crashed worker's entries are
    ///     not stuck forever. Null when unclaimed.
    /// </summary>
    public DateTimeOffset? ClaimedUntil { get; set; }
}
