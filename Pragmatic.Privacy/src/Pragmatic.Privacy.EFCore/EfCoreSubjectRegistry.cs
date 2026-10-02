using System.Text;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Cryptography;
using Pragmatic.Privacy.EFCore.Entities;

namespace Pragmatic.Privacy.EFCore;

/// <summary>
///     EF Core-backed <see cref="ISubjectRegistry" />.
/// </summary>
/// <remarks>
///     Identities are stored encrypted and found through a keyed blind index — an encrypted value
///     cannot be searched, because the same identity encrypts differently every time.
/// </remarks>
internal sealed class EfCoreSubjectRegistry(
    PrivacyDbContext db,
    ISecretEncryptor encryptor,
    ISubjectLookupKeyProvider lookupKeys,
    TimeProvider timeProvider) : ISubjectRegistry
{
    public async ValueTask<string> GetOrCreateReferenceAsync(
        string subjectType, string identifier, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var index = await BlindIndexAsync(subjectType, identifier, ct).ConfigureAwait(false);

        var existing = await db.Subjects
            .FirstOrDefaultAsync(s => s.LookupIndex == index, ct)
            .ConfigureAwait(false);

        if (existing is not null)
            return existing.SubjectRef;

        // An identity that was erased and comes back gets a NEW reference, and is not recognised as
        // the person it used to be.
        //
        // Recognising it would mean keeping something derived from the identity — a hash, an index,
        // anything that answers "was this person here before". Keeping that is still processing their
        // data, so the erasure would not have been one. The convenience of continuity loses to the
        // property the whole design exists for.
        var record = new SubjectRecord
        {
            SubjectRef = SubjectLookup.NewReference(),
            SubjectType = subjectType,
            LookupIndex = index,
            // The subject reference is the associated data, so an encrypted identity cannot be moved
            // to another subject's row and still decrypt.
            Identifier = null,
            CreatedAt = timeProvider.GetUtcNow()
        };

        record.Identifier = encryptor.EncryptBytes(Encoding.UTF8.GetBytes(identifier), record.SubjectRef);

        db.Subjects.Add(record);

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another caller registered the same identity in between. The unique index on the blind
            // index rejected the duplicate, which is the outcome we want — one reference per person.
            db.ChangeTracker.Clear();

            var winner = await db.Subjects
                .FirstOrDefaultAsync(s => s.LookupIndex == index, ct)
                .ConfigureAwait(false);

            if (winner is null)
                throw;

            return winner.SubjectRef;
        }

        return record.SubjectRef;
    }

    public async ValueTask<string?> FindReferenceAsync(
        string subjectType, string identifier, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var index = await BlindIndexAsync(subjectType, identifier, ct).ConfigureAwait(false);

        return await db.Subjects
            .Where(s => s.LookupIndex == index)
            .Select(s => s.SubjectRef)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async ValueTask<string?> ResolveIdentityAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        var record = await db.Subjects
            .FirstOrDefaultAsync(s => s.SubjectRef == subjectRef, ct)
            .ConfigureAwait(false);

        // Null for both "never existed" and "erased". The caller cannot tell them apart, and that is
        // the point: after an erasure the reference has to stop meaning a person to everyone.
        if (record?.Identifier is null)
            return null;

        return encryptor.TryDecryptBytes(record.Identifier, out var plain, record.SubjectRef)
            ? Encoding.UTF8.GetString(plain)
            : null;
    }

    public async ValueTask<bool> ForgetAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        var record = await db.Subjects
            .FirstOrDefaultAsync(s => s.SubjectRef == subjectRef, ct)
            .ConfigureAwait(false);

        if (record is null || record.ForgottenAt is not null)
            return false;

        // The row survives with neither the identity nor the index. What is left says the reference
        // existed and was erased — which is what an audit trail full of it needs to remain meaningful,
        // while holding nothing that leads back to a person. Nothing identity-derived is kept, which
        // is also why a returning identity cannot be recognised.
        record.LookupIndex = null;
        record.Identifier = null;
        record.ForgottenAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<byte[]> BlindIndexAsync(string subjectType, string identifier, CancellationToken ct)
    {
        var key = await lookupKeys.GetLookupKeyAsync(ct).ConfigureAwait(false);
        return SubjectLookup.BlindIndex(key, subjectType, identifier);
    }
}
