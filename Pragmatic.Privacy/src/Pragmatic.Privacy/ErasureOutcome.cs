namespace Pragmatic.Privacy;

/// <summary>
///     The result of erasing a subject.
/// </summary>
/// <param name="SubjectRef">The subject that was erased.</param>
/// <param name="ErasedCount">How many records were erased across every step.</param>
/// <param name="Retained">What was kept, and why. Empty when the erasure was total.</param>
/// <param name="KeyDestroyed">
///     Whether the subject's encryption key was destroyed — which is what reaches copies no update can,
///     backups included.
/// </param>
/// <param name="IdentityForgotten">
///     Whether the link between the reference and the person was broken. False when a hold kept the
///     subject identifiable on purpose.
/// </param>
public sealed record ErasureOutcome(
    string SubjectRef,
    int ErasedCount,
    IReadOnlyList<RetainedItem> Retained,
    bool KeyDestroyed,
    bool IdentityForgotten)
{
    /// <summary>
    ///     True when nothing was retained.
    /// </summary>
    /// <remarks>
    ///     A false value is not a failure. "Erased 12 records, 2 retained under a fiscal obligation" is
    ///     the ordinary outcome of an erasure request, and it is complete — what would be wrong is
    ///     retaining without saying so.
    /// </remarks>
    public bool IsTotal => Retained.Count == 0;
}
