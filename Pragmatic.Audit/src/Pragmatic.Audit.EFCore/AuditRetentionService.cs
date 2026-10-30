using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Audit.EFCore;

/// <summary>
///     Bounds the trail's growth by discarding whole sealed segments, and declaring the gap.
/// </summary>
/// <remarks>
///     <para>
///         <b>Never individual entries.</b> Removing entries from a sealed segment changes its Merkle
///         root, so the segment would fail verification forever afterwards — retention would be
///         indistinguishable from tampering, which destroys the only thing the trail is for.
///     </para>
///     <para>
///         The gap is recorded as a <see cref="PrunedRange" /> carrying the hash the following segment
///         still links to. Verification steps over a declared gap; an undeclared one is a broken chain.
///     </para>
/// </remarks>
public sealed class AuditRetentionService(AuditDbContext db, TimeProvider timeProvider) : IAuditRetention
{
    /// <summary>
    ///     Discards sealed segments that opened longer ago than <paramref name="retention" />.
    /// </summary>
    /// <returns>The declared gap, or <see langword="null" /> when nothing was old enough.</returns>
    public async Task<PrunedRange?> PruneOlderThanAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = timeProvider.GetUtcNow() - retention;

        var due = await db.Segments
            .Where(s => s.SealedAt != null && s.OpenedAt < cutoff)
            .OrderBy(s => s.SegmentId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (due.Count == 0)
            return null;

        // Declared before anything is deleted: a segment without a hash refuses the prune while the rows are still there.
        var range = AuditChain.Gap(due, timeProvider.GetUtcNow());
        var ids = due.ConvertAll(s => s.SegmentId);

        await db.Entries.Where(e => ids.Contains(e.SegmentId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.Segments.Where(s => ids.Contains(s.SegmentId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        db.PrunedRanges.Add(range);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return range;
    }
}
