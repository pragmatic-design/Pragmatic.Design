using Pragmatic.Persistence.Sql;

namespace Pragmatic.Audit.Engines.Sql;

/// <summary>
///     Appends entries to the trail on the Sql engine, through the trail's boundary's session: what EF Core's <c>EfCoreAuditTrail</c> does on
///     <c>AuditDbContext</c>. Appends take no lock and coordinate with nobody: the segment is derived from the timestamp and <c>Seq</c> comes
///     from the database identity.
/// </summary>
/// <remarks>
///     Not an <see cref="ITransactionalAuditTrail" />: the session cannot join a transaction somebody else's connection owns, and the contract
///     forbids writing outside it. A producer that needs the entry in its own transaction writes on the engines through a save (an
///     <c>[Audited]</c> entity), or with <c>AdoNetAuditTrail</c> on its connection.
/// </remarks>
public sealed class SqlAuditTrail(SqlSession session, AuditEntryPreparer preparer) : IAuditTrail
{
    /// <inheritdoc />
    public async ValueTask RecordAsync(AuditEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // Shaping lives in AuditEntryPreparer so every writer agrees on it: the segment, the redacted detail.
        preparer.Prepare(entry);

        var segment = await session.Query<AuditSegmentRow>().FirstOrDefaultAsync((s, id) => s.SegmentId == id, entry.SegmentId, ct).ConfigureAwait(false);
        if (segment is null)
            await session.InsertAsync(new AuditSegmentRow { SegmentId = entry.SegmentId, OpenedAt = entry.OccurredAt.UtcTicks }, ct).ConfigureAwait(false);
        else if (segment.SealedAt is not null)
            throw AuditStore.SealedSegment(entry);

        var row = AuditRecords.From(new AuditEntryRow(), entry);
        await session.InsertAsync(row, ct).ConfigureAwait(false);
        entry.Seq = row.Seq;
    }
}
