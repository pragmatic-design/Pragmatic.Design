using Pragmatic.Persistence.Sql;

namespace Pragmatic.Audit.Engines.Sql;

/// <summary>
///     Reads the trail on the Sql engine and re-verifies its sealed segments, through the trail's boundary's session: what EF Core's
///     <c>EfCoreAuditTrailReader</c> does, the chain's rules <see cref="AuditChain" />'s.
/// </summary>
public sealed class SqlAuditTrailReader(SqlSession session) : IAuditTrailReader
{
    /// <inheritdoc />
    public async Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filter = SqlSpecifications.ToPredicate(AuditEntryRowSpecifications.Of(query));

        var total = await session.Query<AuditEntryRow>().Matching(filter).CountAsync(ct).ConfigureAwait(false);
        var rows = await session.Query<AuditEntryRow>().Matching(filter).OrderByDescending(e => e.Seq)
            .Skip(Math.Max(0, query.Offset)).Take(Math.Clamp(query.Limit, 1, AuditStore.MaxLimit)).ToListAsync(ct).ConfigureAwait(false);

        return new AuditPage([.. rows.Select(AuditRecords.ToEntry)], total);
    }

    /// <inheritdoc />
    public async Task<IntegrityReport> VerifyAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default)
    {
        var segments = await session.Query<AuditSegmentRow>()
            .Where((s, a) => s.SealedAt != null && s.OpenedAt >= a.From && s.OpenedAt < a.Until, (From: from.UtcTicks, Until: until.UtcTicks))
            .OrderBy(s => s.SegmentId).ToListAsync(ct).ConfigureAwait(false);
        var pruned = await session.Query<AuditPrunedRangeRow>().OrderBy(p => p.FromSegmentId).ToListAsync(ct).ConfigureAwait(false);

        return await AuditChain.VerifyAsync([.. segments.Select(AuditRecords.ToSegment)], [.. pruned.Select(AuditRecords.ToRange)],
            (id, token) => LeafHashesAsync(session, id, token), ct).ConfigureAwait(false);
    }

    /// <summary>A segment's entries hashed in <c>Seq</c> order: the order sealing and verification must both use.</summary>
    internal static async Task<List<byte[]>> LeafHashesAsync(SqlSession session, string segmentId, CancellationToken ct)
    {
        var rows = await session.Query<AuditEntryRow>().Where((e, id) => e.SegmentId == id, segmentId).OrderBy(e => e.Seq).ToListAsync(ct).ConfigureAwait(false);
        return rows.ConvertAll(row => AuditHashing.HashEntry(AuditRecords.ToEntry(row)));
    }
}
