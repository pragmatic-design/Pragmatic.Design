namespace Pragmatic.Privacy;

/// <summary>
///     Carries out an erasure across every step that holds a subject's data, in the order that makes a
///     crash recoverable.
/// </summary>
/// <remarks>
///     <para>
///         The orchestrator exists because erasure is not a per-entity operation. It spans entities,
///         modules, stored files and keys, and the <b>order</b> is what decides whether an interruption
///         leaves something recoverable or something lost. No single step can own that.
///     </para>
///     <para>
///         Order: steps first, ascending — stored files before the rows that point at them — then the
///         subject's key, then the link between the reference and the person. Each stage is only safe to
///         run once the previous one has committed.
///     </para>
/// </remarks>
public sealed class ErasureOrchestrator(
    IEnumerable<IErasureStep> steps,
    ISubjectRegistry registry,
    ILegalHoldStore? legalHolds = null) : ISubjectErasure
{
    /// <summary>
    ///     Erases everything held about <paramref name="subjectRef" /> that is not under an obligation
    ///     to keep.
    /// </summary>
    /// <param name="subjectRef">The subject's opaque reference.</param>
    /// <param name="destroyKeyAsync">
    ///     Destroys the subject's encryption key. Supplied by the caller rather than taken as a
    ///     dependency, so this module does not require the cryptography package to be present for
    ///     erasures that do not use key destruction.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async ValueTask<ErasureOutcome> EraseAsync(
        string subjectRef,
        Func<string, CancellationToken, ValueTask<bool>>? destroyKeyAsync = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        // A step whose data is erased by destroying a key cannot erase it on its own, and does not try:
        // the generated plan skips a DestroyKey property because clearing it is not how it is erased.
        // With no destroyer that field ends up neither cleared nor unreadable, and answering
        // KeyDestroyed: false while calling the erasure complete would be a subject-rights answer that
        // is wrong, which is worse than one that fails.
        //
        // Checked here, before anything is touched, for the same reason holds are: refusing halfway
        // through leaves the earlier steps committed and the subject half-erased.
        if (destroyKeyAsync is null)
        {
            var needsAKey = steps.FirstOrDefault(s => s.RequiresKeyDestruction);
            if (needsAKey is not null)
                throw new InvalidOperationException(
                    $"Erasure step '{needsAKey.Name}' erases data by destroying the subject's key, and no "
                    + "key destroyer was supplied. Pass destroyKeyAsync — for example "
                    + "ISubjectKeyStore.DestroyAsync from Pragmatic.Cryptography — or classify those "
                    + "properties with an erasure strategy this deployment can carry out.");
        }

        var retained = new List<RetainedItem>();

        // Holds are checked before anything is touched. Discovering a live dispute halfway through an
        // erasure is discovering it too late: the earlier steps have already committed.
        if (legalHolds is not null)
            retained.AddRange(await legalHolds.GetActiveHoldsAsync(subjectRef, ct).ConfigureAwait(false));

        var erased = 0;

        foreach (var step in steps.OrderBy(s => s.Order))
        {
            var result = await step.EraseAsync(subjectRef, ct).ConfigureAwait(false);
            erased += result.ErasedCount;
            retained.AddRange(result.Retained);
        }

        // The key goes unless something kept needs it. Destroying it while a hold is in force would
        // make the very data the hold protects unreadable — an erasure defeating the obligation that
        // was supposed to override it. But a value kept in the clear does not need it: keeping the key
        // for it would leave one identifier retained for the audit holding readable every field whose
        // erasure is the key's destruction, with the outcome saying only KeyDestroyed = false.
        var keyDestroyed = false;
        if (!retained.Any(r => r.RequiresKey) && destroyKeyAsync is not null)
            keyDestroyed = await destroyKeyAsync(subjectRef, ct).ConfigureAwait(false);

        // The link goes last. Break it earlier and the remaining steps would be working on a subject
        // nothing can resolve — and a failure part-way would leave data behind that nobody can find
        // again to finish erasing.
        var forgotten = false;
        if (retained.Count == 0)
            forgotten = await registry.ForgetAsync(subjectRef, ct).ConfigureAwait(false);

        return new ErasureOutcome(subjectRef, erased, retained, keyDestroyed, forgotten);
    }
}
