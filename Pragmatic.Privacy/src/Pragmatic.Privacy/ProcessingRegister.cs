namespace Pragmatic.Privacy;

/// <summary>
///     The Article 30 record of processing activities: the half derived from the code, joined to the
///     half only the controller can declare.
/// </summary>
/// <param name="ControllerName">Who the controller is.</param>
/// <param name="ControllerContact">How to reach them.</param>
/// <param name="GeneratedAt">When the register was produced.</param>
/// <param name="Activities">The processing activities, one per type holding personal data.</param>
/// <param name="Operations">
///     The operations through which that data is processed. Empty until the sources contributing them
///     are regenerated, which is why it is defaulted rather than required.
/// </param>
/// <remarks>
///     The point of generating it is the direction of truth. A register maintained by hand describes the
///     system as somebody remembered it, and drifts from the first column added afterwards; this one is
///     a projection of what is actually there, recomputed at every build. What it will not do is invent
///     the parts it cannot observe.
/// </remarks>
public sealed record ProcessingRegister(
    string ControllerName,
    string ControllerContact,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ProcessingActivity> Activities,
    IReadOnlyList<ProcessingOperation>? Operations = null)
{
    /// <summary>The operations that process personal data, never null.</summary>
    public IReadOnlyList<ProcessingOperation> ProcessingOperations => Operations ?? [];

    /// <summary>
    ///     Activities still missing a declared purpose.
    /// </summary>
    /// <remarks>
    ///     Surfaced as a list rather than hidden, so "the register is incomplete, and here is exactly
    ///     where" is available before an authority asks the same question.
    /// </remarks>
    public IReadOnlyList<ProcessingActivity> Incomplete =>
        [.. Activities.Where(a => a.NeedsDeclaredPurpose)];

    /// <summary>
    ///     Operations still missing a declared purpose — the list somebody can actually work through.
    /// </summary>
    /// <remarks>
    ///     The difference from <see cref="Incomplete" /> is not cosmetic. "Declare a purpose for the
    ///     <c>Member</c> type" has no single right answer, because different operations touch it for
    ///     different reasons; "declare a purpose for these seven operations" is a task with an end.
    /// </remarks>
    public IReadOnlyList<ProcessingOperation> IncompleteOperations =>
        [.. ProcessingOperations.Where(o => o.NeedsDeclaredPurpose)];

    /// <summary>The operations that <b>read</b> personal data.</summary>
    /// <remarks>
    ///     Writes are recorded by the audit trail on every path; reads are recorded nowhere. This is the
    ///     list to look at when deciding which reads deserve a run-time record, rather than choosing
    ///     between all of them and none.
    /// </remarks>
    public IReadOnlyList<ProcessingOperation> Reads =>
        [.. ProcessingOperations.Where(o => o.Access == ProcessingAccess.Read)];

    /// <summary>
    ///     The reads that leave no trace — personal data an operation returns without anything recording
    ///     that it did.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not a defect list. Recording every read is the wrong default: reads outnumber writes by
    ///         orders of magnitude, and a trail that holds all of them cannot be searched when it matters.
    ///         Most entries here are meant to be here.
    ///     </para>
    ///     <para>
    ///         What it does is make the choice visible. "Who looked at this person's record" cannot be
    ///         answered retroactively — the evidence was written at the time or it does not exist — so
    ///         the useful moment to decide is while reading this list, not after the question is asked.
    ///     </para>
    /// </remarks>
    public IReadOnlyList<ProcessingOperation> UnrecordedReads =>
        [.. Reads.Where(o => !o.Recorded)];

    /// <summary>True when every activity carries a declared purpose.</summary>
    public bool IsComplete => Incomplete.Count == 0;

    /// <summary>Every category of personal data processed anywhere, deduplicated.</summary>
    public IReadOnlyList<string> AllCategories =>
        [.. Activities.SelectMany(a => a.Categories).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>Whether any activity touches special categories, which carry stricter obligations.</summary>
    public bool ProcessesSpecialCategories =>
        Activities.Any(a => a.Categories.Contains("Special", StringComparer.Ordinal));
}
