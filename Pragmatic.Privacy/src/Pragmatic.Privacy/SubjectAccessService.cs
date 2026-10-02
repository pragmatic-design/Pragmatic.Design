namespace Pragmatic.Privacy;

/// <summary>
///     Assembles everything held about a subject, for an access request (Article 15) or a portability
///     one (Article 20).
/// </summary>
/// <remarks>
///     One service for both, because the data is the same and only the format differs. Splitting them
///     would create two paths that have to be kept in step, and the one used less often would fall
///     behind — which shows up as an export missing a field somebody added last month.
/// </remarks>
public sealed class SubjectAccessService(
    IEnumerable<IPersonalDataSource> sources,
    IConsentStore consents,
    TimeProvider timeProvider) : ISubjectAccess
{
    /// <summary>
    ///     Collects everything held about the subject.
    /// </summary>
    /// <remarks>
    ///     Retained data is included. A field kept under a fiscal obligation is still the subject's data
    ///     and still part of the honest answer to "what do you hold about me" — withholding it would
    ///     answer a different question from the one that was asked.
    /// </remarks>
    public async ValueTask<SubjectDataExport> CollectAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        var categories = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
            StringComparer.Ordinal);

        foreach (var source in sources)
        {
            var records = await source.CollectAsync(subjectRef, ct).ConfigureAwait(false);
            if (records.Count == 0)
                continue;

            // Two sources claiming the same category name would silently overwrite each other, and the
            // export would be short by exactly the records nobody noticed were missing.
            if (categories.ContainsKey(source.Category))
                throw new InvalidOperationException(
                    $"Two personal-data sources both report the category '{source.Category}'. " +
                    "Categories name what the subject is shown, so they have to be distinct — otherwise " +
                    "one source's records replace the other's and the export is quietly incomplete.");

            categories[source.Category] = records;
        }

        var consentHistory = await consents.GetHistoryAsync(subjectRef, ct).ConfigureAwait(false);

        return new SubjectDataExport(subjectRef, timeProvider.GetUtcNow(), categories, consentHistory);
    }
}
