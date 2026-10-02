namespace Pragmatic.Audit;

/// <summary>
///     Brings an entry into the shape the trail requires, before any store sees it.
/// </summary>
/// <remarks>
///     <para>
///         Extracted because there is more than one way to write to the trail and only one way an entry
///         may be shaped. A writer that skips this produces entries with no segment — which cannot be
///         sealed, and therefore cannot be verified — and with an unredacted detail, which is the one
///         field able to reintroduce a personal value into a trail designed never to hold one.
///     </para>
///     <para>
///         Deliberately free of storage concerns: it stamps, assigns and redacts, and knows nothing
///         about connections, transactions or providers. That is what lets the EF writer, an ADO.NET
///         writer and an EF interceptor adding rows to the application's own context all agree on what
///         an entry is.
///     </para>
/// </remarks>
public sealed class AuditEntryPreparer(
    IAuditDetailRedactor redactor,
    IAuditSegmentNaming naming,
    TimeProvider timeProvider)
{
    /// <summary>
    ///     Validates the entry, stamps it if needed, assigns its segment and redacts its detail.
    /// </summary>
    /// <returns>The same instance, mutated in place.</returns>
    /// <exception cref="ArgumentException">The operation is missing.</exception>
    public AuditEntry Prepare(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Greppable and stable: an operation assembled at runtime cannot be found by searching the
        // source, which is the first thing anyone does when a trail entry needs explaining.
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Operation);

        if (entry.OccurredAt == default)
            entry.OccurredAt = timeProvider.GetUtcNow();

        entry.SegmentId = naming.SegmentFor(entry.OccurredAt);

        // Applied here, not asked of the caller. The trail's promise is that an entry holds no personal
        // value; a promise every caller has to remember to keep is not a promise.
        if (entry.Detail is { Length: > 0 })
            entry.Detail = redactor.Redact(entry.Detail);

        return entry;
    }
}
