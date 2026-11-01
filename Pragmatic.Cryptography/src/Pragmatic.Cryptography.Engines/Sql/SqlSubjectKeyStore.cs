using System.Data.Common;
using Pragmatic.Persistence.Sql;

namespace Pragmatic.Cryptography.Engines.Sql;

/// <summary>
///     The per-subject keys on the Sql engine, through the boundary's session that holds the subjects: what EF Core's
///     <c>EfCoreSubjectKeyStore</c> does, on the same table. The store and the resolver by key id at once.
/// </summary>
public sealed class SqlSubjectKeyStore : ISubjectKeyStore, IKeyResolver
{
    private readonly SqlSession _session;
    private readonly ISecretEncryptor _master;
    private readonly TimeProvider _timeProvider;
    private readonly SubjectKeyCache? _cache;

    internal SqlSubjectKeyStore(SqlSession session, ISecretEncryptor master, TimeProvider timeProvider, SubjectKeyCache? cache)
    {
        _session = session;
        _master = master;
        _timeProvider = timeProvider;
        _cache = cache;
    }

    /// <inheritdoc />
    public async ValueTask<EncryptionKey> GetOrCreateAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        if (await BySubjectAsync(subjectRef, ct).ConfigureAwait(false) is { } existing)
            return Unwrap(existing);

        var (key, wrapped) = SubjectKeyMaterial.Create(_master, subjectRef);
        try
        {
            await _session.InsertAsync(new SubjectKeyRow { SubjectRef = subjectRef, KeyId = key.KeyId, WrappedKey = wrapped, CreatedAt = _timeProvider.GetUtcNow() }, ct)
                .ConfigureAwait(false);
        }
        catch (DbException)
        {
            // Another caller created the subject's key in between: the primary key refused the duplicate, which is the outcome wanted (one key per
            // subject). The winner's key is the answer; any other failure has no winner and stays as it is.
            var winner = await BySubjectAsync(subjectRef, ct).ConfigureAwait(false);
            if (winner is null)
                throw;
            return Unwrap(winner);
        }

        return key;
    }

    /// <inheritdoc />
    public async ValueTask<KeyDestructionReport> DestroyAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        var row = await BySubjectAsync(subjectRef, ct).ConfigureAwait(false) ?? throw SubjectKeyMaterial.NoKey(subjectRef);
        if (row.DestroyedAt is { } already)
            return new KeyDestructionReport(subjectRef, row.KeyId, already, AlreadyDestroyed: true);

        // The row stays, without the key: its id is what tells a later reader that a value under it is erased rather than tampered with.
        var destroyedAt = _timeProvider.GetUtcNow();
        row.WrappedKey = null;
        row.DestroyedAt = destroyedAt;
        await _session.UpdateAsync(row, ct).ConfigureAwait(false);

        // After the row is written, a second line: a read checks the status before it resolves a key, so another process's cache is harmless.
        _cache?.Evict(row.KeyId);
        return new KeyDestructionReport(subjectRef, row.KeyId, destroyedAt, AlreadyDestroyed: false);
    }

    /// <inheritdoc />
    public async ValueTask<SubjectKeyStatus> GetStatusByKeyIdAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        var row = await ByKeyIdAsync(keyId, ct).ConfigureAwait(false);
        return row is null ? SubjectKeyStatus.Unknown : row.DestroyedAt is null ? SubjectKeyStatus.Live : SubjectKeyStatus.Destroyed;
    }

    /// <inheritdoc />
    public async ValueTask<EncryptionKey?> FindByIdAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        // Unknown and destroyed both resolve to null: the resolver answers "can you decrypt this", the store tells the two apart.
        var row = await ByKeyIdAsync(keyId, ct).ConfigureAwait(false);
        return row is null || row.DestroyedAt is not null ? null : Unwrap(row);
    }

    private EncryptionKey Unwrap(SubjectKeyRow row) => SubjectKeyMaterial.Unwrap(_master, row.SubjectRef, row.KeyId, row.WrappedKey, row.DestroyedAt);

    private ValueTask<SubjectKeyRow?> BySubjectAsync(string subjectRef, CancellationToken ct)
        => _session.Query<SubjectKeyRow>().Where((k, r) => k.SubjectRef == r, subjectRef).FirstOrDefaultAsync(ct);

    private ValueTask<SubjectKeyRow?> ByKeyIdAsync(string keyId, CancellationToken ct)
        => _session.Query<SubjectKeyRow>().Where((k, id) => k.KeyId == id, keyId).FirstOrDefaultAsync(ct);
}
