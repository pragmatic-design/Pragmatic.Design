namespace Pragmatic.Privacy;

/// <summary>
///     One place that holds personal data about a subject, able to hand it over.
/// </summary>
/// <remarks>
///     <para>
///         The mirror of <see cref="IErasureStep" />: the same set of places, asked to produce rather
///         than to remove. Keeping them as separate contracts is deliberate — an access request and an
///         erasure request touch the same data with different rules, and a single interface would
///         invite one of them to be implemented as an afterthought of the other.
///     </para>
///     <para>
///         Contributed rather than discovered. The generated extractors are static types, and finding
///         them at runtime would mean reflection — which this framework does not use, and which would
///         make the export silently incomplete under trimming.
///     </para>
/// </remarks>
public interface IPersonalDataSource
{
    /// <summary>What this source covers, as the subject will see it named in their export.</summary>
    string Category { get; }

    /// <summary>
    ///     Returns everything held about the subject here, one entry per record.
    /// </summary>
    ValueTask<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectAsync(
        string subjectRef, CancellationToken ct = default);
}
