namespace Pragmatic.Cryptography;

/// <summary>
///     The set of AES keys available to the encryptor: one <see cref="Current" /> key used for new writes,
///     plus any previous keys still needed to decrypt secrets encrypted before a rotation. Decryption tries
///     the key whose id matches the ciphertext (versioned format), falling back to trying every key for
///     legacy (unversioned) ciphertext.
/// </summary>
public sealed class EncryptionKeyRing : IKeyResolver
{
    private readonly Dictionary<string, EncryptionKey> _byId;

    /// <summary>The key used to encrypt new values.</summary>
    public EncryptionKey Current { get; }

    /// <summary>All keys, current first then previous — the order legacy ciphertext is tried in.</summary>
    public IReadOnlyList<EncryptionKey> All { get; }

    /// <summary>
    ///     Builds a ring from the current key and zero or more previous keys. Duplicate ids (same key
    ///     material) are collapsed; the current key always wins.
    /// </summary>
    public EncryptionKeyRing(EncryptionKey current, IReadOnlyList<EncryptionKey>? previous = null)
    {
        ArgumentNullException.ThrowIfNull(current);

        Current = current;
        _byId = new Dictionary<string, EncryptionKey>(StringComparer.Ordinal) { [current.KeyId] = current };

        var ordered = new List<EncryptionKey> { current };
        if (previous is not null)
            foreach (var key in previous)
                if (key is not null && _byId.TryAdd(key.KeyId, key))
                    ordered.Add(key);

        All = ordered;
    }

    /// <summary>Finds the key with the given id, or <c>null</c> when the ring does not contain it.</summary>
    public EncryptionKey? FindById(string keyId)
        => _byId.TryGetValue(keyId, out var key) ? key : null;

    /// <summary>
    ///     <see cref="IKeyResolver" /> over the ring. The ring is already in memory, so this never
    ///     actually awaits — it exists so a caller can treat an in-memory ring and a store-backed
    ///     resolver the same way.
    /// </summary>
    public ValueTask<EncryptionKey?> FindByIdAsync(string keyId, CancellationToken ct = default)
        => ValueTask.FromResult(FindById(keyId));
}
