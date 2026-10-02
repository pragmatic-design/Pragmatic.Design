using System.Collections.Concurrent;

namespace Pragmatic.Cryptography;

/// <summary>
///     Caches unwrapped per-subject key material, so a hot subject does not pay an AES-GCM unwrap on
///     every read.
/// </summary>
/// <remarks>
///     <para>
///         <b>This cache deliberately holds key material and nothing else.</b> It does not cache whether
///         a subject still exists, and that is the whole design: a read asks the store for the subject's
///         status on every call, so a destroyed key is reported as erased before this cache is ever
///         consulted. A stale entry here cannot resurrect erased data.
///     </para>
///     <para>
///         That matters most across instances. If instance A destroys a key, instance B's copy of this
///         cache is still warm — and harmless, because B checks the status too. Caching the status
///         instead would have needed distributed invalidation, which is precisely the kind of mechanism
///         that fails quietly.
///     </para>
///     <para>
///         Entries are evicted locally on destruction as well. That is a second line, not the first.
///     </para>
/// </remarks>
public sealed class SubjectKeyCache
{
    private readonly ConcurrentDictionary<string, EncryptionKey> _byKeyId = new(StringComparer.Ordinal);

    /// <summary>Maximum number of cached keys. Past this, the cache stops admitting new entries.</summary>
    /// <remarks>
    ///     A hard cap rather than an eviction policy: the point is to bound memory holding key material,
    ///     and refusing to grow is a simpler guarantee than choosing a victim. Reads still work, they
    ///     just pay the unwrap.
    /// </remarks>
    public int Capacity { get; init; } = 10_000;

    /// <summary>Number of keys currently held.</summary>
    public int Count => _byKeyId.Count;

    /// <summary>Looks up cached material by key id.</summary>
    public bool TryGet(string keyId, out EncryptionKey key) => _byKeyId.TryGetValue(keyId, out key!);

    /// <summary>Caches material, unless the cache is at capacity.</summary>
    public void Set(string keyId, EncryptionKey key)
    {
        if (_byKeyId.Count >= Capacity && !_byKeyId.ContainsKey(keyId))
            return;

        _byKeyId[keyId] = key;
    }

    /// <summary>Drops a single key.</summary>
    public void Evict(string keyId) => _byKeyId.TryRemove(keyId, out _);

    /// <summary>Drops everything.</summary>
    public void Clear() => _byKeyId.Clear();
}
