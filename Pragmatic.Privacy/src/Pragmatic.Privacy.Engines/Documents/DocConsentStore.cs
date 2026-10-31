using Pragmatic.Persistence.Documents;

namespace Pragmatic.Privacy.Engines.Documents;

/// <summary>The consent store on the documents engine, through the registry's boundary's session: what EF Core's <c>EfCoreConsentStore</c> does.</summary>
public sealed class DocConsentStore : IConsentStore
{
    private readonly DocSession _session;
    private readonly TimeProvider _timeProvider;

    internal DocConsentStore(DocSession session, TimeProvider timeProvider)
    {
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async ValueTask GrantAsync(string subjectRef, string purpose, string noticeVersion, string? source = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(noticeVersion);

        var id = ConsentDocument.IdOf(subjectRef, purpose, noticeVersion);
        var existing = await _session.Query<ConsentDocument>().FirstOrDefaultAsync((c, i) => c.Id == i, id, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            // Re-granting after a withdrawal reactivates the same fact. The original grant time is kept: when they first agreed is part of the record.
            existing.WithdrawnAt = null;
            await _session.ReplaceAsync(existing, ct).ConfigureAwait(false);
            return;
        }

        await _session.InsertAsync(new ConsentDocument
        {
            Id = id,
            SubjectRef = subjectRef,
            Purpose = purpose,
            NoticeVersion = noticeVersion,
            GrantedAt = _timeProvider.GetUtcNow().UtcTicks,
            Source = source,
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> WithdrawAsync(string subjectRef, string purpose, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        // Every notice version for the purpose: withdrawing means "stop", not "stop under the version I happen to be looking at".
        var active = await _session.Query<ConsentDocument>()
            .Where((c, k) => c.SubjectRef == k.SubjectRef && c.Purpose == k.Purpose && c.WithdrawnAt == null, (SubjectRef: subjectRef, Purpose: purpose))
            .ToListAsync(ct).ConfigureAwait(false);
        if (active.Count == 0)
            return false;

        foreach (var document in active)
        {
            document.WithdrawnAt = now.UtcTicks;
            await _session.ReplaceAsync(document, ct).ConfigureAwait(false);
        }

        return true;
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsGrantedAsync(string subjectRef, string purpose, string noticeVersion, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(noticeVersion);

        // The exact notice version: consent under an older notice does not cover processing a newer one describes.
        return await _session.Query<ConsentDocument>()
            .AnyAsync((c, i) => c.Id == i && c.WithdrawnAt == null, ConsentDocument.IdOf(subjectRef, purpose, noticeVersion), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ConsentRecord>> GetHistoryAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        var documents = await _session.Query<ConsentDocument>().Where((c, r) => c.SubjectRef == r, subjectRef).OrderBy(c => c.GrantedAt).ToListAsync(ct).ConfigureAwait(false);
        return documents.ConvertAll(ConsentRecords.ToRecord<ConsentDocument>);
    }
}
