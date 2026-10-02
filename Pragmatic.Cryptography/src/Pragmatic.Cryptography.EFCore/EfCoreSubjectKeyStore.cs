using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Cryptography.EFCore.Entities;

namespace Pragmatic.Cryptography.EFCore;

/// <summary>
///     EF Core-backed <see cref="ISubjectKeyStore" />. Also serves as the <see cref="IKeyResolver" /> for
///     per-subject keys: reads look up by the key id carried in the ciphertext header.
/// </summary>
/// <remarks>
///     Subject keys are stored wrapped by the master key ring, with the subject reference as associated
///     data — so a wrapped key lifted from one row cannot be planted on another and still unwrap.
/// </remarks>
internal sealed class EfCoreSubjectKeyStore(
    CryptographyDbContext db,
    ISecretEncryptor master,
    TimeProvider timeProvider,
    SubjectKeyCache? cache = null) : ISubjectKeyStore, IKeyResolver
{
    private const int KeySizeBytes = 32;

    public async ValueTask<EncryptionKey> GetOrCreateAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        var existing = await db.SubjectKeys
            .FirstOrDefaultAsync(r => r.SubjectRef == subjectRef, ct)
            .ConfigureAwait(false);

        if (existing is not null)
            return Unwrap(existing);

        var material = RandomNumberGenerator.GetBytes(KeySizeBytes);
        var key = EncryptionKey.FromMaterial(material);

        db.SubjectKeys.Add(new SubjectKeyRecord
        {
            SubjectRef = subjectRef,
            KeyId = key.KeyId,
            WrappedKey = master.EncryptBytes(material, subjectRef),
            CreatedAt = timeProvider.GetUtcNow()
        });

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another caller created the same subject's key between the read and the insert. The
            // primary key rejected the duplicate, which is the desired outcome — one key per subject.
            // Drop the losing entity and return the winner rather than surfacing a write conflict for
            // something that is already in the state the caller asked for.
            db.ChangeTracker.Clear();

            var winner = await db.SubjectKeys
                .FirstOrDefaultAsync(r => r.SubjectRef == subjectRef, ct)
                .ConfigureAwait(false);

            if (winner is null)
                throw;

            return Unwrap(winner);
        }

        return key;
    }

    public async ValueTask<KeyDestructionReport> DestroyAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        var record = await db.SubjectKeys
            .FirstOrDefaultAsync(r => r.SubjectRef == subjectRef, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No encryption key exists for subject '{subjectRef}'. Destroying a key that was never " +
                "created would report an erasure that did not happen.");

        if (record.DestroyedAt is { } already)
            return new KeyDestructionReport(subjectRef, record.KeyId, already, AlreadyDestroyed: true);

        var destroyedAt = timeProvider.GetUtcNow();

        // The row stays, minus the key: KeyId is what tells a later reader that this ciphertext is
        // erased rather than tampered with.
        record.WrappedKey = null;
        record.DestroyedAt = destroyedAt;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Evict after the row is committed. This is a second line, not the first: a read checks the
        // subject's status against the store before it resolves any key, so a cache this call cannot
        // reach — another process's — is harmless. Evicting here just avoids holding destroyed key
        // material in memory longer than necessary.
        cache?.Evict(record.KeyId);

        return new KeyDestructionReport(subjectRef, record.KeyId, destroyedAt, AlreadyDestroyed: false);
    }

    public async ValueTask<SubjectKeyStatus> GetStatusByKeyIdAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        var state = await db.SubjectKeys
            .Where(r => r.KeyId == keyId)
            .Select(r => new { r.DestroyedAt })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (state is null)
            return SubjectKeyStatus.Unknown;

        return state.DestroyedAt is null ? SubjectKeyStatus.Live : SubjectKeyStatus.Destroyed;
    }

    public async ValueTask<EncryptionKey?> FindByIdAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        var record = await db.SubjectKeys
            .FirstOrDefaultAsync(r => r.KeyId == keyId, ct)
            .ConfigureAwait(false);

        // Unknown and destroyed both resolve to null here — the resolver only answers "can you decrypt
        // this". Telling the two apart is the store's job, and the reason the destroyed row is kept.
        return record is null || record.DestroyedAt is not null ? null : Unwrap(record);
    }

    private EncryptionKey Unwrap(SubjectKeyRecord record)
    {
        if (record.DestroyedAt is { } destroyedAt)
            throw new SubjectKeyDestroyedException(record.SubjectRef, destroyedAt);

        if (record.WrappedKey is null)
            throw new InvalidOperationException(
                $"Subject '{record.SubjectRef}' has no wrapped key but is not marked destroyed. The row " +
                "is inconsistent and unwrapping it would be a guess.");

        if (!master.TryDecryptBytes(record.WrappedKey, out var material, record.SubjectRef))
            throw new CryptographicException(
                $"Could not unwrap the key for subject '{record.SubjectRef}': the master key ring no " +
                "longer authenticates it. The master key changed without re-wrapping, or the row was tampered with.");

        var key = EncryptionKey.FromMaterial(material);

        // The id is a fingerprint of the material, so the two must agree. If they do not, the row's id
        // no longer describes the key it holds — and since reads resolve by id, every lookup would
        // silently return the wrong key. Fail here rather than hand one out.
        if (!string.Equals(key.KeyId, record.KeyId, StringComparison.Ordinal))
            throw new CryptographicException(
                $"Subject '{record.SubjectRef}' stores key id '{record.KeyId}' but its wrapped material " +
                $"fingerprints to '{key.KeyId}'. The row is inconsistent.");

        return key;
    }
}
