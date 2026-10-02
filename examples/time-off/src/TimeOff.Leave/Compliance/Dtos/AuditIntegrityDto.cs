using Pragmatic.Audit;

namespace TimeOff.Leave.Dtos;

/// <summary>
///     Whether the audit trail is as it was written: how many sealed segments were re-hashed, which ones
///     no longer match, and the gaps retention declared.
/// </summary>
/// <remarks>
///     Only sealed segments can be checked. <see cref="SegmentsChecked" /> is part of the answer for that
///     reason: "intact" over no segments at all says nothing.
/// </remarks>
public sealed record AuditIntegrityDto(bool IsIntact, int SegmentsChecked, IReadOnlyList<string> BrokenSegmentIds, int DeclaredGaps)
{
    internal static AuditIntegrityDto From(IntegrityReport report) =>
        new(report.IsIntact, report.SegmentsChecked, report.BrokenSegmentIds, report.PrunedRanges.Count);
}
