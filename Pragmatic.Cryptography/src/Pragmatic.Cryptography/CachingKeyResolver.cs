namespace Pragmatic.Cryptography;

/// <summary>
///     Wraps an <see cref="IKeyResolver" /> with <see cref="SubjectKeyCache" />, so repeated reads of the
///     same subject skip the unwrap.
/// </summary>
/// <remarks>
///     <b>This is not an erasure-safe path on its own.</b> It answers "what is the material for this key
///     id", not "does this subject still exist" — and a warm entry outlives a destruction until it is
///     evicted. Read subject data through <see cref="ISubjectDataProtector" />, which checks the
///     subject's status against the store before it resolves anything.
/// </remarks>
public sealed class CachingKeyResolver(IKeyResolver inner, SubjectKeyCache cache) : IKeyResolver
{
    public async ValueTask<EncryptionKey?> FindByIdAsync(string keyId, CancellationToken ct = default)
    {
        if (cache.TryGet(keyId, out var cached))
            return cached;

        var key = await inner.FindByIdAsync(keyId, ct).ConfigureAwait(false);
        if (key is not null)
            cache.Set(keyId, key);

        return key;
    }
}
