using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Privacy.EFCore;

/// <summary>
///     EF Core-backed <see cref="IConsentStore" />.
/// </summary>
internal sealed class EfCoreConsentStore(PrivacyDbContext db, TimeProvider timeProvider) : IConsentStore
{
    public async ValueTask GrantAsync(
        string subjectRef, string purpose, string noticeVersion, string? source = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(noticeVersion);

        var existing = await db.Consents
            .FirstOrDefaultAsync(
                c => c.SubjectRef == subjectRef && c.Purpose == purpose && c.NoticeVersion == noticeVersion, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // Re-granting after a withdrawal reactivates the same fact: the subject agreed to this
            // purpose under this notice. The original grant time is kept, because when they first
            // agreed is part of the record.
            existing.WithdrawnAt = null;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        db.Consents.Add(new ConsentRecord
        {
            SubjectRef = subjectRef,
            Purpose = purpose,
            NoticeVersion = noticeVersion,
            GrantedAt = timeProvider.GetUtcNow(),
            Source = source
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> WithdrawAsync(
        string subjectRef, string purpose, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        // Every notice version for the purpose, not just the current one: a subject withdrawing
        // consent means "stop", not "stop under the version I happen to be looking at".
        var active = await db.Consents
            .Where(c => c.SubjectRef == subjectRef && c.Purpose == purpose && c.WithdrawnAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (active.Count == 0)
            return false;

        foreach (var record in active)
            record.WithdrawnAt = now;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> IsGrantedAsync(
        string subjectRef, string purpose, string noticeVersion, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(noticeVersion);

        // Matched on the exact notice version. Consent given under an older notice does not cover
        // processing described by a newer one — that is the whole reason the version is stored.
        return await db.Consents
            .AnyAsync(
                c => c.SubjectRef == subjectRef
                     && c.Purpose == purpose
                     && c.NoticeVersion == noticeVersion
                     && c.WithdrawnAt == null,
                ct)
            .ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<ConsentRecord>> GetHistoryAsync(
        string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        return await db.Consents
            .AsNoTracking()
            .Where(c => c.SubjectRef == subjectRef)
            .OrderBy(c => c.GrantedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
