namespace Pragmatic.Privacy;

/// <summary>
///     One unit of an erasure: everything that has to happen to a subject's data in one place.
/// </summary>
/// <remarks>
///     <para>
///         Implementations are normally thin wrappers over the generated erasure plans — one per entity
///         reachable from a data subject. Splitting the work this way is what lets the orchestrator own
///         the <b>order</b>, which is the part that has to be right.
///     </para>
///     <para>
///         A step reports what it retained rather than deciding what to do about it. Whether a partial
///         erasure is an acceptable answer is a question about the request, not about one table.
///     </para>
/// </remarks>
public interface IErasureStep
{
    /// <summary>What this step covers, for the record and for ordering.</summary>
    string Name { get; }

    /// <summary>
    ///     Lower runs first.
    /// </summary>
    /// <remarks>
    ///     <b>Stored files must be reclaimed before the rows that point at them.</b> Delete the row
    ///     first and crash, and the file is orphaned with nobody able to find it again — personal data
    ///     left behind permanently. In the other order a crash leaves a row for the next run to retry.
    ///     Steps that touch storage therefore order themselves below steps that touch their rows.
    /// </remarks>
    int Order { get; }

    /// <summary>
    ///     True when some of this step's data is erased by destroying the subject's key, rather than by
    ///     clearing a column.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ A step that says so <b>cannot erase that field itself</b>, and does not try: the
    ///         generated plan skips a <c>DestroyKey</c> property deliberately, because clearing it is
    ///         not how it is erased. So the orchestrator has to be told, or an erasure with no key
    ///         destroyer would leave the data neither cleared nor unreadable and report success anyway.
    ///     </para>
    ///     <para>
    ///         Default false: most deployments encrypt nothing per subject, and an erasure that clears
    ///         columns is complete without any key at all.
    ///     </para>
    /// </remarks>
    bool RequiresKeyDestruction => false;

    /// <summary>Erases this step's data for the subject, reporting anything deliberately kept.</summary>
    ValueTask<ErasureStepResult> EraseAsync(string subjectRef, CancellationToken ct = default);
}
