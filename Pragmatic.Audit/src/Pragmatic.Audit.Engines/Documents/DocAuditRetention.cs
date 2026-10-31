using Pragmatic.Persistence.Documents;

namespace Pragmatic.Audit.Engines.Documents;

/// <summary>
///     Bounds the trail's growth on the documents engine by discarding whole sealed segments, and declaring the gap: what EF Core's
///     <c>AuditRetentionService</c> does.
/// </summary>
public sealed class DocAuditRetention(DocSession session, TimeProvider timeProvider) : IAuditRetention
{
    /// <inheritdoc />
    public async Task<PrunedRange?> PruneOlderThanAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = (timeProvider.GetUtcNow() - retention).UtcTicks;
        var due = await session.Query<AuditSegmentDocument>().Where((s, c) => s.SealedAt != null && s.OpenedAt < c, cutoff)
            .OrderBy(s => s.SegmentId).ToListAsync(ct).ConfigureAwait(false);
        if (due.Count == 0)
            return null;

        // Declared before anything is deleted: a segment without a hash refuses the prune while the documents are still there.
        var range = AuditChain.Gap([.. due.Select(AuditRecords.ToSegment)], timeProvider.GetUtcNow());
        var ids = due.ConvertAll(s => s.SegmentId);

        await session.Query<AuditEntryDocument>().Where((e, a) => a.Contains(e.SegmentId), ids).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await session.Query<AuditSegmentDocument>().Where((s, a) => a.Contains(s.SegmentId), ids).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await session.InsertAsync(AuditRecords.From(new AuditPrunedRangeDocument(), range), ct).ConfigureAwait(false);
        return range;
    }
}
