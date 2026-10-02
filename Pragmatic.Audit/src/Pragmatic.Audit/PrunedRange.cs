namespace Pragmatic.Audit;

/// <summary>
///     A gap in the chain left by retention, recorded so verification can tell it apart from tampering.
/// </summary>
/// <param name="FromSegmentId">First segment removed.</param>
/// <param name="UntilSegmentId">Last segment removed.</param>
/// <param name="PrunedAt">When the removal happened.</param>
/// <param name="LinkHash">
///     The hash the segment after the gap still links to. The chain is not broken by the removal — it
///     steps over it, and the step is declared.
/// </param>
public sealed record PrunedRange(
    string FromSegmentId,
    string UntilSegmentId,
    DateTimeOffset PrunedAt,
    byte[] LinkHash);
