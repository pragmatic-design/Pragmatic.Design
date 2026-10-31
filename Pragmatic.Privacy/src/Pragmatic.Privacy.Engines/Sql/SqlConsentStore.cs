using Pragmatic.Persistence.Sql;

namespace Pragmatic.Privacy.Engines.Sql;

/// <summary>The consent store on the Sql engine, through the registry's boundary's session: what EF Core's <c>EfCoreConsentStore</c> does, on the same table.</summary>
public sealed class SqlConsentStore : IConsentStore
{
    private readonly SqlSession _session;
    private readonly TimeProvider _timeProvider;

    internal SqlConsentStore(SqlSession session, TimeProvider timeProvider)
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

        var existing = await _session.Query<ConsentRow>()
            .Where((c, k) => c.SubjectRef == k.SubjectRef && c.Purpose == k.Purpose && c.NoticeVersion == k.NoticeVersion, (SubjectRef: subjectRef, Purpose: purpose, NoticeVersion: noticeVersion))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (existing is not null)
        {
            // Re-granting after a withdrawal reactivates the same fact. The original grant time is kept: when they first agreed is part of the record.
            existing.WithdrawnAt = null;
            await _session.UpdateAsync(existing, ct).ConfigureAwait(false);
            return;
        }

        await _session.InsertAsync(new ConsentRow
        {
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
        var active = await _session.Query<ConsentRow>()
            .Where((c, k) => c.SubjectRef == k.SubjectRef && c.Purpose == k.Purpose && c.WithdrawnAt == null, (SubjectRef: subjectRef, Purpose: purpose))
            .ToListAsync(ct).ConfigureAwait(false);
        if (active.Count == 0)
            return false;

        foreach (var row in active)
        {
            row.WithdrawnAt = now.UtcTicks;
            await _session.UpdateAsync(row, ct).ConfigureAwait(false);
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
        return await _session.Query<ConsentRow>()
            .Where((c, k) => c.SubjectRef == k.SubjectRef && c.Purpose == k.Purpose && c.NoticeVersion == k.NoticeVersion && c.WithdrawnAt == null,
                (SubjectRef: subjectRef, Purpose: purpose, NoticeVersion: noticeVersion))
            .AnyAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ConsentRecord>> GetHistoryAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        var rows = await _session.Query<ConsentRow>().Where((c, r) => c.SubjectRef == r, subjectRef).OrderBy(c => c.GrantedAt).ToListAsync(ct).ConfigureAwait(false);
        return rows.ConvertAll(ConsentRecords.ToRecord<ConsentRow>);
    }
}
