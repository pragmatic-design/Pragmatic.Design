using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Audit.EFCore;

/// <summary>
///     Closes segments whose window has passed and hashes them into the chain.
/// </summary>
/// <remarks>
///     Runs periodically. Sealing is the only writer that touches a segment's hash fields, and it never
///     touches an entry — which is what lets appends stay uncoordinated while the result stays
///     verifiable.
/// </remarks>
public sealed class AuditSealingService(
    AuditDbContext db,
    IAuditSegmentNaming naming,
    TimeProvider timeProvider) : IAuditSealer
{
    /// <summary>
    ///     Seals every open segment whose window closed longer ago than the grace period, oldest first.
    /// </summary>
    /// <returns>The ids of the segments sealed, in the order they were chained.</returns>
    public async Task<IReadOnlyList<string>> SealDueSegmentsAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var sealedIds = new List<string>();

        var open = await db.Segments
            .Where(s => s.SealedAt == null)
            .OrderBy(s => s.SegmentId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        AuditSegment? sealedThisPass = null;

        foreach (var segment in open)
        {
            if (!AuditChain.IsDue(segment, naming, now))
                continue;

            await SealAsync(segment, sealedThisPass, ct).ConfigureAwait(false);
            sealedIds.Add(segment.SegmentId);
            sealedThisPass = segment;
        }

        if (sealedIds.Count > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return sealedIds;
    }

    private async Task SealAsync(AuditSegment segment, AuditSegment? sealedThisPass, CancellationToken ct)
    {
        var hashes = await LeafHashesAsync(db, segment.SegmentId, ct).ConfigureAwait(false);

        // A segment sealed earlier in this pass is not saved yet, so the database still sees it open and
        // the query would link past it: every segment after the first in one pass was chained to the
        // wrong predecessor and verified as broken. Segments are sealed oldest first, so the
        // one sealed just before is the predecessor.
        var previous = sealedThisPass ?? await db.Segments
            .Where(s => s.SealedAt != null && string.Compare(s.SegmentId, segment.SegmentId) < 0)
            .OrderByDescending(s => s.SegmentId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        AuditChain.Seal(segment, previous, hashes, timeProvider.GetUtcNow());
    }

    /// <summary>
    ///     Hashes a segment's entries in <c>Seq</c> order — the same order sealing and verification must
    ///     both use, or every segment would fail to re-verify.
    /// </summary>
    internal static async Task<List<byte[]>> LeafHashesAsync(AuditDbContext db, string segmentId, CancellationToken ct)
    {
        var entries = await db.Entries
            .AsNoTracking()
            .Where(e => e.SegmentId == segmentId)
            .OrderBy(e => e.Seq)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return entries.ConvertAll(AuditHashing.HashEntry);
    }
}
