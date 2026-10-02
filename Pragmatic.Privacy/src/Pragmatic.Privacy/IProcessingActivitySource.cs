namespace Pragmatic.Privacy;

/// <summary>
///     Supplies the processing activities the code knows about.
/// </summary>
/// <remarks>
///     Implemented over the metadata the generator emits per assembly, so the register follows the code
///     rather than a document somebody has to remember to update. Contributed rather than discovered,
///     for the same reason as the data sources: finding the generated metadata at runtime would mean
///     reflection.
/// </remarks>
public interface IProcessingActivitySource
{
    /// <summary>The activities declared in one assembly.</summary>
    ValueTask<IReadOnlyList<ProcessingActivity>> GetActivitiesAsync(CancellationToken ct = default);

    /// <summary>The operations in one assembly that process personal data.</summary>
    /// <remarks>
    ///     <para>
    ///         The half <see cref="GetActivitiesAsync" /> cannot answer. That one describes types — what
    ///         is held and how it is erased; this one describes what the application <em>does</em> with
    ///         them, which is what Article 30 calls an activity and where a purpose can actually be
    ///         declared.
    ///     </para>
    ///     <para>
    ///         Defaulted to empty rather than added to the interface outright: a source that predates it
    ///         keeps compiling and contributes nothing, which is the truthful answer for one that has no
    ///         operations to report.
    ///     </para>
    /// </remarks>
    ValueTask<IReadOnlyList<ProcessingOperation>> GetOperationsAsync(CancellationToken ct = default)
        => new([]);
}
