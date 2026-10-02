using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Audit.EFCore;

/// <summary>
///     Reads the trail and re-verifies its sealed segments.
/// </summary>
internal sealed class EfCoreAuditTrailReader(AuditDbContext db) : IAuditTrailReader
{
    /// <summary>Upper bound on a page, so a query cannot pull the whole trail into memory.</summary>
    private const int MaxLimit = 1000;

    public async Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = db.Entries.AsNoTracking().AsQueryable();

        if (query.From is { } from) q = q.Where(e => e.OccurredAt >= from);
        if (query.Until is { } until) q = q.Where(e => e.OccurredAt < until);
        if (query.Category is { } category) q = q.Where(e => e.Category == category);
        if (query.SubjectRef is { Length: > 0 } s) q = q.Where(e => e.SubjectRef == s);
        if (query.ActorRef is { Length: > 0 } a) q = q.Where(e => e.ActorRef == a);
        if (query.TenantId is { Length: > 0 } t) q = q.Where(e => e.TenantId == t);
        if (query.CorrelationId is { Length: > 0 } c) q = q.Where(e => e.CorrelationId == c);
        if (query.Outcome is { } outcome) q = q.Where(e => e.Outcome == outcome);
        if (query.TargetType is { Length: > 0 } targetType) q = q.Where(e => e.TargetType == targetType);
        if (query.TargetId is { Length: > 0 } targetId) q = q.Where(e => e.TargetId == targetId);

        var total = await q.LongCountAsync(ct).ConfigureAwait(false);

        var entries = await q
            .OrderByDescending(e => e.Seq)
            .Skip(Math.Max(0, query.Offset))
            .Take(Math.Clamp(query.Limit, 1, MaxLimit))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new AuditPage(entries, total);
    }

    public async Task<IntegrityReport> VerifyAsync(
        DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default)
    {
        var segments = await db.Segments
            .AsNoTracking()
            .Where(s => s.SealedAt != null && s.OpenedAt >= from && s.OpenedAt < until)
            .OrderBy(s => s.SegmentId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var pruned = await db.PrunedRanges
            .AsNoTracking()
            .OrderBy(p => p.FromSegmentId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var broken = new List<string>();
        byte[]? expectedPrevious = null;
        var first = true;

        foreach (var segment in segments)
        {
            // Recompute the root from the entries as they are now. If a single entry was altered,
            // added, removed or reordered, the root moves and this is where it shows.
            var hashes = await AuditSealingService.LeafHashesAsync(db, segment.SegmentId, ct).ConfigureAwait(false);
            var root = AuditHashing.MerkleRoot(hashes);

            var rootMatches = segment.MerkleRoot is not null && root.SequenceEqual(segment.MerkleRoot);

            // The link is only checked from the second segment on: the first one in the range legitimately
            // links to something outside it. Checking it anyway would report every partial range as broken.
            var linkMatches = first
                || SequenceEqualOrBothNull(segment.PreviousHash, expectedPrevious)
                || IsAcrossDeclaredGap(pruned, segment, expectedPrevious);

            var ownHashMatches = segment.MerkleRoot is not null
                && segment.SegmentHash is not null
                && AuditHashing.HashSegment(segment.PreviousHash, segment.MerkleRoot, segment.SegmentId)
                    .SequenceEqual(segment.SegmentHash);

            if (!rootMatches || !linkMatches || !ownHashMatches)
                broken.Add(segment.SegmentId);

            expectedPrevious = segment.SegmentHash;
            first = false;
        }

        return new IntegrityReport(segments.Count, broken, pruned);
    }

    private static bool SequenceEqualOrBothNull(byte[]? a, byte[]? b)
        => (a is null && b is null) || (a is not null && b is not null && a.SequenceEqual(b));

    /// <summary>
    ///     A segment whose predecessor was pruned links to the hash recorded in the gap declaration
    ///     rather than to the segment now preceding it. A declared gap is not a broken chain.
    /// </summary>
    private static bool IsAcrossDeclaredGap(List<PrunedRange> pruned, AuditSegment segment, byte[]? expectedPrevious)
        => pruned.Exists(p =>
            string.CompareOrdinal(p.UntilSegmentId, segment.SegmentId) < 0
            && SequenceEqualOrBothNull(segment.PreviousHash, p.LinkHash)
            && expectedPrevious is not null);
}
