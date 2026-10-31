using Pragmatic.Persistence.Documents;

namespace Pragmatic.Audit.Engines.Documents;

/// <summary>
///     Reads the trail on the documents engine and re-verifies its sealed segments, through the trail's boundary's session: what EF Core's
///     <c>EfCoreAuditTrailReader</c> does, the chain's rules <see cref="AuditChain" />'s. Newest first is <c>(Seq, Id)</c> descending.
/// </summary>
public sealed class DocAuditTrailReader(DocSession session) : IAuditTrailReader
{
    /// <inheritdoc />
    public async Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filter = DocSpecifications.ToPredicate(AuditEntryDocumentSpecifications.Of(query));

        var total = await session.Query<AuditEntryDocument>().Matching(filter).CountAsync(ct).ConfigureAwait(false);
        var documents = await session.Query<AuditEntryDocument>().Matching(filter).OrderByDescending(e => e.Seq).ThenByDescending(e => e.Id)
            .Skip(Math.Max(0, query.Offset)).Take(Math.Clamp(query.Limit, 1, AuditStore.MaxLimit)).ToListAsync(ct).ConfigureAwait(false);

        return new AuditPage([.. documents.Select(AuditRecords.ToEntry)], total);
    }

    /// <inheritdoc />
    public async Task<IntegrityReport> VerifyAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default)
    {
        var segments = await session.Query<AuditSegmentDocument>()
            .Where((s, a) => s.SealedAt != null && s.OpenedAt >= a.From && s.OpenedAt < a.Until, (From: from.UtcTicks, Until: until.UtcTicks))
            .OrderBy(s => s.SegmentId).ToListAsync(ct).ConfigureAwait(false);
        var pruned = await session.Query<AuditPrunedRangeDocument>().OrderBy(p => p.FromSegmentId).ToListAsync(ct).ConfigureAwait(false);

        return await AuditChain.VerifyAsync([.. segments.Select(AuditRecords.ToSegment)], [.. pruned.Select(AuditRecords.ToRange)],
            (id, token) => DocAuditStore.LeafHashesAsync(session, id, token), ct).ConfigureAwait(false);
    }
}
