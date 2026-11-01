using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Mongo;

namespace Pragmatic.Cryptography.Engines.Documents;

/// <summary>
///     The per-subject keys on the documents engine, through the boundary's session that holds the subjects: what EF Core's
///     <c>EfCoreSubjectKeyStore</c> does, one document per subject. The store and the resolver by key id at once.
/// </summary>
public sealed class DocSubjectKeyStore : ISubjectKeyStore, IKeyResolver
{
    private readonly DocSession _session;
    private readonly ISecretEncryptor _master;
    private readonly TimeProvider _timeProvider;
    private readonly SubjectKeyCache? _cache;

    internal DocSubjectKeyStore(DocSession session, ISecretEncryptor master, TimeProvider timeProvider, SubjectKeyCache? cache)
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
            await _session.InsertAsync(new SubjectKeyDocument
            {
                SubjectRef = subjectRef, KeyId = key.KeyId, WrappedKey = Convert.ToBase64String(wrapped), CreatedAt = _timeProvider.GetUtcNow(),
            }, ct).ConfigureAwait(false);
        }
        catch (Exception taken) when (taken is MongoWriteException { IsDuplicateKey: true } or CosmosException { IsConflict: true })
        {
            // Another caller created the subject's key in between: one key per subject, and the winner's is the answer.
            var winner = await BySubjectAsync(subjectRef, ct).ConfigureAwait(false)
                         ?? throw new InvalidOperationException("The subject's key was taken and then removed.", taken);
            return Unwrap(winner);
        }

        return key;
    }

    /// <inheritdoc />
    public async ValueTask<KeyDestructionReport> DestroyAsync(string subjectRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRef);

        var document = await BySubjectAsync(subjectRef, ct).ConfigureAwait(false) ?? throw SubjectKeyMaterial.NoKey(subjectRef);
        if (document.DestroyedAt is { } already)
            return new KeyDestructionReport(subjectRef, document.KeyId, already, AlreadyDestroyed: true);

        // The document stays, without the key: its id is what tells a later reader that a value under it is erased rather than tampered with.
        var destroyedAt = _timeProvider.GetUtcNow();
        document.WrappedKey = null;
        document.DestroyedAt = destroyedAt;
        await _session.ReplaceAsync(document, ct).ConfigureAwait(false);

        // After the document is written, a second line: a read checks the status before it resolves a key, so another process's cache is harmless.
        _cache?.Evict(document.KeyId);
        return new KeyDestructionReport(subjectRef, document.KeyId, destroyedAt, AlreadyDestroyed: false);
    }

    /// <inheritdoc />
    public async ValueTask<SubjectKeyStatus> GetStatusByKeyIdAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        var document = await ByKeyIdAsync(keyId, ct).ConfigureAwait(false);
        return document is null ? SubjectKeyStatus.Unknown : document.DestroyedAt is null ? SubjectKeyStatus.Live : SubjectKeyStatus.Destroyed;
    }

    /// <inheritdoc />
    public async ValueTask<EncryptionKey?> FindByIdAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        // Unknown and destroyed both resolve to null: the resolver answers "can you decrypt this", the store tells the two apart.
        var document = await ByKeyIdAsync(keyId, ct).ConfigureAwait(false);
        return document is null || document.DestroyedAt is not null ? null : Unwrap(document);
    }

    private EncryptionKey Unwrap(SubjectKeyDocument document)
        => SubjectKeyMaterial.Unwrap(_master, document.SubjectRef, document.KeyId,
            document.WrappedKey is null ? null : Convert.FromBase64String(document.WrappedKey), document.DestroyedAt);

    private ValueTask<SubjectKeyDocument?> BySubjectAsync(string subjectRef, CancellationToken ct)
        => _session.Query<SubjectKeyDocument>().FirstOrDefaultAsync((k, r) => k.SubjectRef == r, subjectRef, ct);

    private ValueTask<SubjectKeyDocument?> ByKeyIdAsync(string keyId, CancellationToken ct)
        => _session.Query<SubjectKeyDocument>().FirstOrDefaultAsync((k, id) => k.KeyId == id, keyId, ct);
}
