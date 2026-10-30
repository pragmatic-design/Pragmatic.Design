namespace Pragmatic.Audit;

/// <summary>
///     The chain's rules over a store's rows, whatever store holds them: when a segment is due, how it is sealed, how a range is
///     re-verified, and how a retention gap is declared. Each store reads and writes its own rows and asks these.
/// </summary>
/// <remarks>
///     Sealing and verification must agree to the byte, and so must two stores holding one trail: written once, here, they cannot drift.
///     The leaf hashes a store hands over are its segment's entries, hashed in the order it seals them (the EF Core store: by <c>Seq</c>),
///     and verification must be handed them in the same order.
/// </remarks>
public static class AuditChain
{
    /// <summary>
    ///     True when <paramref name="segment" />'s window closed longer ago than the grace period: a write in flight when the window closed
    ///     can still commit, and sealing over it would leave a real entry outside the root, reported later as tampering.
    /// </summary>
    public static bool IsDue(AuditSegment segment, IAuditSegmentNaming naming, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(naming);
        return naming.WindowEnd(segment.SegmentId) + naming.GracePeriod <= now;
    }

    /// <summary>
    ///     Seals <paramref name="segment" /> over its entries' <paramref name="leafHashes" />, linked to <paramref name="previous" />, the
    ///     sealed segment before it (null for the first of the trail).
    /// </summary>
    public static void Seal(AuditSegment segment, AuditSegment? previous, IReadOnlyList<byte[]> leafHashes, DateTimeOffset sealedAt)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(leafHashes);

        segment.EntryCount = leafHashes.Count;
        segment.MerkleRoot = AuditHashing.MerkleRoot(leafHashes);
        segment.PreviousHash = previous?.SegmentHash;
        segment.SegmentHash = AuditHashing.HashSegment(segment.PreviousHash, segment.MerkleRoot, segment.SegmentId);
        segment.SealedAt = sealedAt;
    }

    /// <summary>
    ///     Re-verifies <paramref name="sealedSegments" /> (sealed, in segment order): each one's root recomputed from its entries as they are
    ///     now, its own hash, and its link to the one before it, or across a gap <paramref name="pruned" /> declares.
    /// </summary>
    /// <param name="sealedSegments">The sealed segments of the range, ordered by segment id.</param>
    /// <param name="pruned">Every declared gap, ordered by its first segment.</param>
    /// <param name="leafHashes">A segment's entries as they are now, hashed in sealing order.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<IntegrityReport> VerifyAsync(
        IReadOnlyList<AuditSegment> sealedSegments,
        IReadOnlyList<PrunedRange> pruned,
        Func<string, CancellationToken, Task<List<byte[]>>> leafHashes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sealedSegments);
        ArgumentNullException.ThrowIfNull(pruned);
        ArgumentNullException.ThrowIfNull(leafHashes);

        var broken = new List<string>();
        byte[]? expectedPrevious = null;
        var first = true;

        foreach (var segment in sealedSegments)
        {
            // Recompute the root from the entries as they are now. If a single entry was altered,
            // added, removed or reordered, the root moves and this is where it shows.
            var hashes = await leafHashes(segment.SegmentId, ct).ConfigureAwait(false);
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

        return new IntegrityReport(sealedSegments.Count, broken, pruned);
    }

    /// <summary>
    ///     The gap retention leaves when it discards <paramref name="due" /> (sealed, in segment order): the hash the segment after it still
    ///     links to. The declaration outlives what it describes: it is what turns a hole into a documented step.
    /// </summary>
    /// <exception cref="InvalidOperationException">The last segment is marked sealed but has no hash: nothing could link across the gap.</exception>
    public static PrunedRange Gap(IReadOnlyList<AuditSegment> due, DateTimeOffset prunedAt)
    {
        ArgumentNullException.ThrowIfNull(due);
        if (due.Count == 0)
            throw new ArgumentException("A gap covers at least one segment.", nameof(due));

        var last = due[^1];
        if (last.SegmentHash is null)
            throw new InvalidOperationException(
                $"Segment '{last.SegmentId}' is marked sealed but has no hash. Pruning it would leave a gap " +
                "nothing can link across, so the chain could never be verified again.");

        return new PrunedRange(due[0].SegmentId, last.SegmentId, prunedAt, last.SegmentHash);
    }

    private static bool SequenceEqualOrBothNull(byte[]? a, byte[]? b)
        => (a is null && b is null) || (a is not null && b is not null && a.SequenceEqual(b));

    /// <summary>
    ///     A segment whose predecessor was pruned links to the hash recorded in the gap declaration
    ///     rather than to the segment now preceding it. A declared gap is not a broken chain.
    /// </summary>
    private static bool IsAcrossDeclaredGap(IReadOnlyList<PrunedRange> pruned, AuditSegment segment, byte[]? expectedPrevious)
        => pruned.Any(p =>
            string.CompareOrdinal(p.UntilSegmentId, segment.SegmentId) < 0
            && SequenceEqualOrBothNull(segment.PreviousHash, p.LinkHash)
            && expectedPrevious is not null);
}
