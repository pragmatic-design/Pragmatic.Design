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

        return await AuditChain.VerifyAsync(segments, pruned, (id, token) => AuditSealingService.LeafHashesAsync(db, id, token), ct)
            .ConfigureAwait(false);
    }
}
