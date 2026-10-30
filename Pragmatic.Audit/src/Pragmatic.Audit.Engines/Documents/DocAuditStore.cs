using Pragmatic.Persistence.Documents;

namespace Pragmatic.Audit.Engines.Documents;

/// <summary>What the documents engine's phase and store share: opening a segment, an entry's document, a segment's leaf hashes.</summary>
internal static class DocAuditStore
{
    /// <summary>The entry's segment, inserted when it is the first of its window; refused when sealed.</summary>
    public static async Task OpenAsync(DocSession session, AuditEntry entry, CancellationToken ct)
    {
        var segment = await session.Query<AuditSegmentDocument>().FirstOrDefaultAsync((s, id) => s.SegmentId == id, entry.SegmentId, ct).ConfigureAwait(false);
        if (segment is null)
            await session.InsertAsync(new AuditSegmentDocument { SegmentId = entry.SegmentId, OpenedAt = entry.OccurredAt.UtcTicks }, ct).ConfigureAwait(false);
        else if (segment.SealedAt is not null)
            throw AuditStore.SealedSegment(entry);
    }

    /// <summary>
    ///     The document of a prepared entry: its <c>Seq</c> the entry's instant in ticks, its id the tie-break between two entries of one
    ///     instant (owner's decision: the documents engine has no identity, and no writer coordinates with another).
    /// </summary>
    public static AuditEntryDocument Document(AuditEntry entry)
    {
        var document = AuditRecords.From(new AuditEntryDocument { Id = Guid.NewGuid() }, entry);
        document.Seq = entry.OccurredAt.UtcTicks;
        entry.Seq = document.Seq;
        return document;
    }

    /// <summary>A segment's entries hashed in <c>(Seq, Id)</c> order: the order sealing and verification must both use.</summary>
    public static async Task<List<byte[]>> LeafHashesAsync(DocSession session, string segmentId, CancellationToken ct)
    {
        var documents = await session.Query<AuditEntryDocument>().Where((e, id) => e.SegmentId == id, segmentId)
            .OrderBy(e => e.Seq).ThenBy(e => e.Id).ToListAsync(ct).ConfigureAwait(false);
        return documents.ConvertAll(document => AuditHashing.HashEntry(AuditRecords.ToEntry(document)));
    }
}
