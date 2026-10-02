namespace Pragmatic.Configuration.Providers;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

/// <summary>
///     Default in-memory configuration store for development and testing.
///     Also serves as a base pattern for local providers (JSON, environment variables).
/// </summary>
public sealed class InMemoryConfigurationStore : IConfigurationStore
{
    private readonly ConcurrentDictionary<string, string> _values = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _tenantValues = new();

    // Pub/sub: each WatchAsync call registers its channel together with the key prefix it cares
    // about. Filtering happens at write time (BroadcastChange) so an unbounded burst of unrelated
    // writes can't evict a relevant change from a subscriber's bounded DropOldest channel.
    private readonly object _subscribersLock = new();
    private readonly List<Subscriber> _subscribers = [];

    private readonly record struct Subscriber(Channel<ConfigurationChange> Channel, string Prefix);

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        _values.TryGetValue(key, out var value);
        return Task.FromResult(value);
    }

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default)
    {
        // Contract (IConfigurationStore): a tenant-scoped read returns ONLY the tenant override,
        // or null when there is none. Base fallback is the resolver's job (cascade) — doing it here
        // would make the resolver unable to tell "no tenant override" from "base value", which
        // silently bypasses the environment overlay layer.
        if (_tenantValues.TryGetValue(tenantId, out var tenantDict) &&
            tenantDict.TryGetValue(key, out var value))
        {
            return Task.FromResult<string?>(value);
        }

        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(
        string prefix, CancellationToken ct = default)
    {
        var result = _values
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return Task.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(
        string prefix, string tenantId, CancellationToken ct = default)
    {
        // Tenant-scoped section returns ONLY the tenant's overrides for this prefix (not base∪tenant).
        // The resolver overlays these on top of base/environment values itself; returning base here
        // would let the tenant tier re-introduce base values over the environment overlay.
        var result = new Dictionary<string, string>();

        if (_tenantValues.TryGetValue(tenantId, out var tenantDict))
        {
            foreach (var kv in tenantDict.Where(kv =>
                         kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                result[kv.Key] = kv.Value;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
    {
        string? oldValue;

        if (tenantId is null)
        {
            _values.TryGetValue(key, out oldValue);
            _values[key] = value;
        }
        else
        {
            var tenantDict = _tenantValues.GetOrAdd(tenantId, _ => new ConcurrentDictionary<string, string>());
            tenantDict.TryGetValue(key, out oldValue);
            tenantDict[key] = value;
        }

        BroadcastChange(new ConfigurationChange(key, oldValue, value, tenantId, DateTimeOffset.UtcNow));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        string? oldValue = null;

        if (tenantId is null)
            _values.TryRemove(key, out oldValue);
        else if (_tenantValues.TryGetValue(tenantId, out var tenantDict))
            tenantDict.TryRemove(key, out oldValue);

        if (oldValue is not null)
        {
            BroadcastChange(new ConfigurationChange(key, oldValue, null, tenantId, DateTimeOffset.UtcNow));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ConfigurationChange> WatchAsync(
        string keyPattern, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var prefix = keyPattern.TrimEnd('*');

        // Bounded to protect the process: a slow watcher drops the oldest (stale)
        // change notification rather than letting the channel grow without limit.
        var channel = Channel.CreateBounded<ConfigurationChange>(
            new BoundedChannelOptions(1024)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

        var subscriber = new Subscriber(channel, prefix);
        lock (_subscribersLock)
        {
            _subscribers.Add(subscriber);
        }

        try
        {
            // Already filtered by prefix at write time, so every buffered change is relevant.
            await foreach (var change in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return change;
        }
        finally
        {
            lock (_subscribersLock)
            {
                _subscribers.Remove(subscriber);
            }

            channel.Writer.TryComplete();
        }
    }

    /// <summary>
    ///     Broadcasts a change to the subscribers whose key prefix matches, so a subscriber's
    ///     bounded channel only ever buffers changes it actually cares about.
    /// </summary>
    private void BroadcastChange(ConfigurationChange change)
    {
        lock (_subscribersLock)
        {
            foreach (var subscriber in _subscribers)
            {
                if (change.Key.StartsWith(subscriber.Prefix, StringComparison.OrdinalIgnoreCase))
                    subscriber.Channel.Writer.TryWrite(change);
            }
        }
    }
}
