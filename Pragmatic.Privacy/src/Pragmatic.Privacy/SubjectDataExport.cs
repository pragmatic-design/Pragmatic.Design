namespace Pragmatic.Privacy;

/// <summary>
///     Everything held about a subject, assembled for an access or portability request.
/// </summary>
/// <param name="SubjectRef">The subject's opaque reference.</param>
/// <param name="GeneratedAt">When the export was produced — an export is a snapshot, not a live view.</param>
/// <param name="Categories">The data, grouped by the source that holds it.</param>
/// <param name="Consents">
///     What the subject has agreed to, withdrawn consents included. Part of the answer to "what do you
///     hold about me": the record of a withdrawal is itself data about them.
/// </param>
public sealed record SubjectDataExport(
    string SubjectRef,
    DateTimeOffset GeneratedAt,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> Categories,
    IReadOnlyList<ConsentRecord> Consents)
{
    /// <summary>Total number of records across every category.</summary>
    public int RecordCount => Categories.Values.Sum(records => records.Count);

    /// <summary>True when nothing at all is held about this subject.</summary>
    public bool IsEmpty => RecordCount == 0 && Consents.Count == 0;
}
