using Pragmatic.Persistence.Documents;

namespace Pragmatic.Audit.Engines.Documents;

/// <summary>
///     Closes the trail's due segments on the documents engine and hashes them into the chain: what EF Core's <c>AuditSealingService</c> does,
///     the chain's rules <see cref="AuditChain" />'s. The only writer that touches a segment's hash fields, and it never touches an entry.
/// </summary>
public sealed class DocAuditSealer(DocSession session, IAuditSegmentNaming naming, TimeProvider timeProvider) : IAuditSealer
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SealDueSegmentsAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var open = await session.Query<AuditSegmentDocument>().Where(s => s.SealedAt == null).OrderBy(s => s.SegmentId).ToListAsync(ct).ConfigureAwait(false);

        var due = open.Where(document => AuditChain.IsDue(AuditRecords.ToSegment(document), naming, now)).ToList();
        if (due.Count == 0)
            return [];

        // The first due segment links to the latest sealed one before it (by OpenedAt, see SqlAuditSealer); each after it to the one this pass
        // sealed just before.
        var previous = await session.Query<AuditSegmentDocument>().Where((s, opened) => s.SealedAt != null && s.OpenedAt < opened, due[0].OpenedAt)
            .OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(ct).ConfigureAwait(false) is { } before
                ? AuditRecords.ToSegment(before)
                : null;

        var sealedIds = new List<string>();
        foreach (var document in due)
        {
            var segment = AuditRecords.ToSegment(document);
            AuditChain.Seal(segment, previous, await DocAuditStore.LeafHashesAsync(session, segment.SegmentId, ct).ConfigureAwait(false), timeProvider.GetUtcNow());
            await session.ReplaceAsync(AuditRecords.Sealed(document, segment), ct).ConfigureAwait(false);
            sealedIds.Add(segment.SegmentId);
            previous = segment;
        }

        return sealedIds;
    }
}
