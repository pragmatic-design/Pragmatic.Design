using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Pragmatic.Agent.KV;

/// <summary>
///     Thread-safe in-memory KV store with Lamport clock versioning, CAS, prefix queries, and watch.
///     Persistence is handled by <see cref="KvFilePersistence"/> (periodic flush to disk).
/// </summary>
internal sealed class KvStore
{
    private readonly ConcurrentDictionary<string, KvEntry> _entries = new(StringComparer.Ordinal);

    // What deletes left behind. Without them a write an Agent gossips back after missing the
    // delete finds no newer version to lose to, and the key comes back everywhere.
    private readonly ConcurrentDictionary<string, KvTombstone> _tombstones = new(StringComparer.Ordinal);
    private readonly List<WatchSubscription> _watchers = [];
    private readonly Lock _watchLock = new();
    // Serialises the version-check + dictionary-write in Set() to make CAS atomic.
    private readonly Lock _writeLock = new();
    private long _clock; // Lamport clock

    // Secrets are encrypted AT REST in the store itself, not only at the socket boundary. This makes
    // gossip replication necessarily carry ciphertext for secret/* keys (gossip reads the stored
    // value verbatim) and keeps plaintext secret material out of the wire and the on-disk snapshot.
    private readonly IKvSecretProtector _secretEncryptor;

    public KvStore(IKvSecretProtector? secretEncryptor = null)
        => _secretEncryptor = secretEncryptor ?? NoEncryption.Instance;

    /// <summary>
    ///     True for keys whose values are encrypted at rest (the <c>secret/</c> namespace).
    /// </summary>
    /// <remarks>
    ///     This protects only material that genuinely transits the daemon's own KV; it is <b>not</b> the
    ///     application secret store. Application secrets belong in an <c>ISecretStore</c> (Key Vault / Vault
    ///     / cloud Secrets Manager) and should be referenced from configuration as <c>secret://{key}</c>,
    ///     resolved locally per host — so secret material never enters the KV / gossip / snapshot at all.
    ///     The app-facing config path writes under <c>config/</c> (plaintext), never <c>secret/</c>.
    /// </remarks>
    public static bool IsSecretKey(string key) => key.StartsWith("secret/", StringComparison.Ordinal);

    /// <summary>
    ///     Gets a single entry by key. Returns null if not found. Values under <c>secret/</c> are
    ///     decrypted on the way out so callers always observe plaintext.
    /// </summary>
    public KvEntry? Get(string key)
    {
        if (!_entries.TryGetValue(key, out var entry))
            return null;

        if (!IsSecretKey(key))
            return entry;

        return entry with { Value = _secretEncryptor.Decrypt(entry.Value) };
    }

    /// <summary>Gets all entries matching a key prefix.</summary>
    public IReadOnlyList<KvEntry> GetByPrefix(string prefix)
    {
        var results = new List<KvEntry>();
        foreach (var kvp in _entries)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                results.Add(kvp.Value);
        }

        return results;
    }

    /// <summary>
    ///     Sets a KV entry. Returns the new version, or -1 if CAS conflict.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="expectedVersion">If set, only succeeds if current version matches (compare-and-swap).</param>
    /// <param name="owner">The Agent holding the client that wrote an ephemeral entry; null for a plain one.</param>
    public (long Version, bool CasConflict) Set(string key, string value, long? expectedVersion = null, string? owner = null)
    {
        // Encrypt secret/* values before they ever land in the store, so the stored value, the
        // gossip replication payload (NotifyWatchers below) and the disk snapshot all carry ciphertext.
        var storedValue = IsSecretKey(key) ? _secretEncryptor.Encrypt(value) : value;

        KvEntry entry;
        long newVersion;

        lock (_writeLock)
        {
            if (expectedVersion.HasValue)
            {
                // Compare-and-swap: version-check and write must be atomic.
                var existing = _entries.TryGetValue(key, out var current) ? current : null;
                var currentVersion = existing?.Version ?? 0;

                if (currentVersion != expectedVersion.Value)
                    return (-1, true);
            }

            newVersion = Interlocked.Increment(ref _clock);
            entry = new KvEntry(key, storedValue, newVersion, DateTimeOffset.UtcNow, owner);
            _entries[key] = entry;
            _tombstones.TryRemove(key, out _);
        }

        NotifyWatchers(key, storedValue, newVersion, deleted: false, owner);

        return (newVersion, false);
    }

    /// <summary>
    ///     Sets a KV entry with an explicit version (for gossip replication).
    ///     Only applies if the incoming version is newer (LWW).
    /// </summary>
    /// <remarks>
    ///     The <paramref name="value"/> is stored verbatim. For <c>secret/</c> keys the gossiped value
    ///     is already ciphertext (encrypted on the origin node's <see cref="Set"/>), so it must NOT be
    ///     re-encrypted here.
    /// </remarks>
    public bool SetIfNewer(string key, string value, long version, DateTimeOffset updatedAt, string? owner = null)
    {
        // Serialise the read-version-check + write against concurrent Set()/Delete() on the same key
        // so a stale gossip update can't race past the LWW guard.
        lock (_writeLock)
        {
            var existing = _entries.TryGetValue(key, out var current) ? current : null;
            if (existing is not null && existing.Version >= version)
                return false; // Our version is same or newer

            if (_tombstones.TryGetValue(key, out var tombstone) && tombstone.Version >= version)
                return false; // Deleted after this write: it is arriving late, not coming back

            var entry = new KvEntry(key, value, version, updatedAt, owner);
            _entries[key] = entry;
            _tombstones.TryRemove(key, out _);
        }

        // Advance local clock if needed
        AdvanceClock(version);

        NotifyWatchers(key, value, version, deleted: false, owner);
        return true;
    }

    /// <summary>
    ///     Deletes every entry <paramref name="agentId" /> owns: its clients' ephemeral entries, when the
    ///     membership declares it dead. Returns how many were deleted.
    /// </summary>
    /// <remarks>
    ///     Each delete leaves a tombstone like any other, so a peer that has not seen the death yet cannot
    ///     bring the entry back through anti-entropy.
    /// </remarks>
    public int DeleteOwnedBy(string agentId)
    {
        var deleted = 0;
        foreach (var entry in _entries.Values)
        {
            if (!string.Equals(entry.Owner, agentId, StringComparison.Ordinal))
                continue;

            long version;
            lock (_writeLock)
            {
                // Only the entry that was scanned: a key rewritten since then belongs to its new writer.
                if (!_entries.TryRemove(new KeyValuePair<string, KvEntry>(entry.Key, entry)))
                    continue;

                version = Interlocked.Increment(ref _clock);
                _tombstones[entry.Key] = new KvTombstone(entry.Key, version, DateTimeOffset.UtcNow);
            }

            NotifyWatchers(entry.Key, null, version, deleted: true, owner: null);
            deleted++;
        }

        return deleted;
    }

    /// <summary>Deletes a KV entry. Returns true if it existed.</summary>
    public bool Delete(string key)
    {
        long version;
        lock (_writeLock)
        {
            if (!_entries.TryRemove(key, out _))
                return false;

            version = Interlocked.Increment(ref _clock);
            _tombstones[key] = new KvTombstone(key, version, DateTimeOffset.UtcNow);
        }

        NotifyWatchers(key, null, version, deleted: true, owner: null);
        return true;
    }

    /// <summary>
    ///     Version-guarded delete for gossip replication. Mirrors <see cref="SetIfNewer"/>: the delete
    ///     only applies when <paramref name="version"/> is strictly newer than the locally-held entry.
    ///     This prevents a forged or stale gossip delete from wiping a newer key (config-wipe DoS).
    ///     Returns true if an entry was removed.
    /// </summary>
    public bool DeleteIfNewer(string key, long version)
    {
        lock (_writeLock)
        {
            if (!_entries.TryGetValue(key, out var existing))
            {
                // Nothing to delete here yet — but the write it deletes may still be on its way, so the
                // tombstone is kept to refuse it when it arrives.
                if (!_tombstones.TryGetValue(key, out var held) || held.Version < version)
                    _tombstones[key] = new KvTombstone(key, version, DateTimeOffset.UtcNow);
                AdvanceClock(version);
                return false;
            }

            if (existing.Version >= version)
                return false; // Our version is same or newer — ignore the (stale/forged) delete

            _entries.TryRemove(key, out _);
            _tombstones[key] = new KvTombstone(key, version, DateTimeOffset.UtcNow);
        }

        AdvanceClock(version);
        NotifyWatchers(key, null, version, deleted: true, owner: null);
        return true;
    }

    /// <summary>Subscribe to changes on keys matching a prefix.</summary>
    public IDisposable Watch(string prefix, Channel<KvChangeEvent> channel)
    {
        WatchSubscription sub = null!;
        sub = new WatchSubscription(prefix, channel, s =>
        {
            lock (_watchLock) { _watchers.Remove(s); }
        });

        lock (_watchLock)
        {
            _watchers.Add(sub);
        }

        return sub;
    }

    /// <summary>The tombstones deletes left, for anti-entropy to carry.</summary>
    public IReadOnlyList<KvTombstone> GetTombstones() => _tombstones.Values.ToList();

    /// <summary>
    ///     Forgets tombstones of deletes older than <paramref name="olderThan" />. The retention must outlast
    ///     the cluster's convergence: an Agent that still held the key after its tombstone was collected
    ///     could bring it back.
    /// </summary>
    public void CollectTombstones(DateTimeOffset olderThan)
    {
        foreach (var tombstone in _tombstones.Values)
        {
            if (tombstone.DeletedAt < olderThan)
                _tombstones.TryRemove(new KeyValuePair<string, KvTombstone>(tombstone.Key, tombstone));
        }
    }

    /// <summary>Gets a snapshot of all entries (for persistence).</summary>
    public IReadOnlyList<KvEntry> GetAll()
    {
        return _entries.Values.ToList();
    }

    /// <summary>Loads entries from persistence (at startup).</summary>
    public void LoadBulk(IEnumerable<KvEntry> entries)
    {
        foreach (var entry in entries)
        {
            _entries[entry.Key] = entry;
            AdvanceClock(entry.Version);
        }
    }

    /// <summary>Gets the current Lamport clock value.</summary>
    public long CurrentVersion => Interlocked.Read(ref _clock);

    private void AdvanceClock(long incomingVersion)
    {
        // Lamport clock: local = max(local, incoming) + 1 on next write
        long current;
        do
        {
            current = Interlocked.Read(ref _clock);
            if (current >= incomingVersion)
                return;
        } while (Interlocked.CompareExchange(ref _clock, incomingVersion, current) != current);
    }

    private void NotifyWatchers(string key, string? value, long version, bool deleted, string? owner)
    {
        var evt = new KvChangeEvent(key, value, version, deleted, owner);

        lock (_watchLock)
        {
            for (var i = _watchers.Count - 1; i >= 0; i--)
            {
                var sub = _watchers[i];
                if (!key.StartsWith(sub.Prefix, StringComparison.Ordinal))
                    continue;

                if (!sub.Channel.Writer.TryWrite(evt))
                {
                    // Channel full or completed — remove subscription
                    _watchers.RemoveAt(i);
                }
            }
        }
    }

    private sealed class WatchSubscription(string prefix, Channel<KvChangeEvent> channel, Action<WatchSubscription> onDispose) : IDisposable
    {
        public string Prefix => prefix;
        public Channel<KvChangeEvent> Channel => channel;

        public void Dispose()
        {
            channel.Writer.TryComplete();
            // Eagerly remove from the watchers list so completed subscriptions do not
            // accumulate until the next write event.
            onDispose(this);
        }
    }
}

/// <summary>Event emitted when a KV entry changes. <paramref name="Owner" /> is <see cref="KvEntry.Owner" />.</summary>
internal sealed record KvChangeEvent(string Key, string? Value, long Version, bool Deleted, string? Owner = null);
