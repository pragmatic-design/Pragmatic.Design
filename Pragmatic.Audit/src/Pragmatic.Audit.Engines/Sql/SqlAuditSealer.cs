using Pragmatic.Persistence.Sql;

namespace Pragmatic.Audit.Engines.Sql;

/// <summary>
///     Closes the trail's due segments on the Sql engine and hashes them into the chain: what EF Core's <c>AuditSealingService</c> does, the
///     chain's rules <see cref="AuditChain" />'s. The only writer that touches a segment's hash fields, and it never touches an entry.
/// </summary>
public sealed class SqlAuditSealer(SqlSession session, IAuditSegmentNaming naming, TimeProvider timeProvider) : IAuditSealer
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SealDueSegmentsAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var open = await session.Query<AuditSegmentRow>().Where(s => s.SealedAt == null).OrderBy(s => s.SegmentId).ToListAsync(ct).ConfigureAwait(false);

        var due = open.Where(row => AuditChain.IsDue(AuditRecords.ToSegment(row), naming, now)).ToList();
        if (due.Count == 0)
            return [];

        // The first due segment links to the latest sealed one before it; each after it to the one this pass sealed just before (sealing is
        // oldest first). ASSUMPTION: "before" by OpenedAt, which orders segments as their ids do (time-derived windows that do not overlap,
        // each opened inside its own), and is a number the engine compares, where an id is a string it does not.
        var previous = await session.Query<AuditSegmentRow>().Where((s, opened) => s.SealedAt != null && s.OpenedAt < opened, due[0].OpenedAt)
            .OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(ct).ConfigureAwait(false) is { } before
                ? AuditRecords.ToSegment(before)
                : null;

        var sealedIds = new List<string>();
        foreach (var row in due)
        {
            var segment = AuditRecords.ToSegment(row);
            AuditChain.Seal(segment, previous, await SqlAuditTrailReader.LeafHashesAsync(session, segment.SegmentId, ct).ConfigureAwait(false), timeProvider.GetUtcNow());
            await session.UpdateAsync(AuditRecords.Sealed(row, segment), ct).ConfigureAwait(false);
            sealedIds.Add(segment.SegmentId);
            previous = segment;
        }

        return sealedIds;
    }
}
