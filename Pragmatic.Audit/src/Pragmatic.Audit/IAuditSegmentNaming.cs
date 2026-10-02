namespace Pragmatic.Audit;

/// <summary>
///     Decides which segment an entry belongs to, and which segments are old enough to seal.
/// </summary>
/// <remarks>
///     Segment boundaries are derived from time rather than assigned by a counter, so any writer can
///     work out where its entry goes without asking anyone. That is what keeps appends free of
///     coordination.
/// </remarks>
public interface IAuditSegmentNaming
{
    /// <summary>The segment an entry at this instant belongs to.</summary>
    string SegmentFor(DateTimeOffset occurredAt);

    /// <summary>The instant the given segment's window closes.</summary>
    DateTimeOffset WindowEnd(string segmentId);

    /// <summary>
    ///     How long to wait after a window closes before sealing it.
    /// </summary>
    /// <remarks>
    ///     A write already in flight can commit after its window ends. Sealing immediately would leave
    ///     that entry outside the Merkle root, and the next verification would report it as tampering —
    ///     a false integrity alarm, which is worse than no alarm because it teaches people to ignore it.
    /// </remarks>
    TimeSpan GracePeriod { get; }
}
