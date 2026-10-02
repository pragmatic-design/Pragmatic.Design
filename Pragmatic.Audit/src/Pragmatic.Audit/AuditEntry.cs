namespace Pragmatic.Audit;

/// <summary>
///     One record in the audit trail.
/// </summary>
/// <remarks>
///     <para>
///         <b>An entry never holds a personal value.</b> It identifies who and what by reference, and
///         proves a change with a hash rather than by keeping the old value. That is what lets the trail
///         survive an erasure: the record stays complete and verifiable while ceasing to be linkable to
///         a person.
///     </para>
///     <para>
///         There is deliberately no free-form payload field. The trail this one replaces had one —
///         <c>MessageAuditEntry.PayloadJson</c>, which carried message payloads, personal data included,
///         with no redaction. A field that accepts anything eventually receives everything.
///     </para>
/// </remarks>
public sealed class AuditEntry
{
    /// <summary>Monotonic position within the segment. Assigned by the store.</summary>
    public long Seq { get; set; }

    /// <summary>The segment this entry belongs to; sealing works on whole segments.</summary>
    public required string SegmentId { get; set; }

    /// <summary>When it happened.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Broad classification, for querying and retention.</summary>
    public AuditCategory Category { get; set; }

    /// <summary>
    ///     What happened, as a stable greppable constant — never assembled at runtime, or it cannot be
    ///     found by searching the source.
    /// </summary>
    public required string Operation { get; set; }

    /// <summary>Who acted, as a pseudonym. Never an identity.</summary>
    public string? ActorRef { get; set; }

    /// <summary>Whose data it concerned, as a pseudonym. Never an identity.</summary>
    public string? SubjectRef { get; set; }

    /// <summary>
    ///     Whose authority the actor was using, as a pseudonym, when the two differ.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A different axis from <see cref="SubjectRef" />, which names whose <em>data</em> an entry
    ///         concerns. This names the principal a delegated call was made for: an agent for the person
    ///         who started it, a support operator for a customer, a job for the owner of a row.
    ///     </para>
    ///     <para>
    ///         Null when nobody delegated anything, which is the ordinary case — the actor was acting
    ///         for themselves and <see cref="ActorRef" /> says everything there is to say.
    ///     </para>
    ///     <para>
    ///         ⚠️ Appended to the hash rather than left outside it, and appended <b>last</b>: a field
    ///         outside the hash is one an attacker with write access could rewrite while the rest stayed
    ///         verifiable, and any other position would silently invalidate every segment sealed before
    ///         the change. Segments sealed before this field was appended verify as broken — see
    ///         <see cref="AuditHashing" />, which is where that trade is written down.
    ///     </para>
    /// </remarks>
    public string? OnBehalfOfRef { get; set; }

    /// <summary>Tenant scope, when the deployment has one.</summary>
    public string? TenantId { get; set; }

    /// <summary>Ties entries from one logical operation together.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    ///     The business operation that caused this entry, fully qualified — <c>null</c> when the change
    ///     did not happen inside one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Operation" /> says what happened to the row; this says what the application was
    ///         doing. Both are needed and neither substitutes for the other: an incident asks "which
    ///         operation changed this", and <c>Data.EntityUpdated</c> on <c>Member</c> cannot answer it.
    ///     </para>
    ///     <para>
    ///         It is also the join to the Article 30 register, which keys its processing operations by
    ///         the same fully qualified name. Without it the register describes what <em>can</em> touch
    ///         personal data and the trail records that something did, with no way to put the two
    ///         together.
    ///     </para>
    ///     <para>
    ///         Null is a truthful answer, not a gap: a seed, a migration or a background fixup changes
    ///         rows outside any declared operation, and naming one would be an invention.
    ///     </para>
    /// </remarks>
    public string? BusinessOperation { get; set; }

    /// <summary>Type of the thing acted upon, e.g. <c>Order</c>.</summary>
    public string? TargetType { get; set; }

    /// <summary>Key of the thing acted upon.</summary>
    public string? TargetId { get; set; }

    /// <summary>Whether the operation succeeded, was refused, or failed.</summary>
    public AuditOutcome Outcome { get; set; }

    /// <summary>
    ///     Hash of the previous value, when the entry records a change. Proves what the value was to
    ///     anyone who can produce a candidate, without the trail retaining it.
    /// </summary>
    public byte[]? ValueHash { get; set; }

    /// <summary>
    ///     Short human-readable context. Always passes through <see cref="IAuditDetailRedactor" /> on
    ///     the way in — it is the one field that could otherwise reintroduce personal data.
    /// </summary>
    public string? Detail { get; set; }
}
