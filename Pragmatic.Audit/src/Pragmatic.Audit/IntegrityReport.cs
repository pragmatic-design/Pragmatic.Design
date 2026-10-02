namespace Pragmatic.Audit;

/// <summary>
///     The result of re-verifying the sealed segments in a time range.
/// </summary>
/// <param name="SegmentsChecked">How many sealed segments were re-hashed.</param>
/// <param name="BrokenSegmentIds">
///     Segments whose recomputed root no longer matches the one recorded when they were sealed, or whose
///     link to the previous segment does not hold. <b>Naming them is the point</b>: "the trail is broken"
///     is not actionable, "segment 2026-07-30T14 is broken" is.
/// </param>
/// <param name="PrunedRanges">
///     Gaps left by retention, declared rather than silent. A chain with an undeclared hole is
///     indistinguishable from a chain someone cut.
/// </param>
public sealed record IntegrityReport(
    int SegmentsChecked,
    IReadOnlyList<string> BrokenSegmentIds,
    IReadOnlyList<PrunedRange> PrunedRanges)
{
    /// <summary>True when every sealed segment in the range still verifies.</summary>
    public bool IsIntact => BrokenSegmentIds.Count == 0;
}
