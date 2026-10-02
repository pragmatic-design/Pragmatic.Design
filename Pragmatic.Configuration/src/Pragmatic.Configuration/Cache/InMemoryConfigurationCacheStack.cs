namespace Pragmatic.Configuration.Cache;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Pragmatic.Caching;

/// <summary>
///     Minimal in-process <see cref="ICacheStack"/> for configuration values.
///     Used as fallback when <c>Pragmatic.Caching</c> is not referenced.
///     No stampede protection, no L2 distributed.
/// </summary>
internal sealed class InMemoryConfigurationCacheStack : ICacheStack, IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    // Tag -> set of keys index, maintained on every write/removal so tag invalidation is O(keys-per-tag).
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagIndex = new();
    private readonly Timer _cleanupTimer;

    public InMemoryConfigurationCacheStack()
    {
        _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async ValueTask<T> GetOrSetAsync<T>(
        string key, Func<CancellationToken, ValueTask<T>> factory,
        CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            if (entry.Value is T typed)
                return typed;
            // Type mismatch — evict stale entry and repopulate
            _cache.TryRemove(key, out _);
        }

        // Multiple concurrent callers may all reach here; all execute the factory and the last write wins.
        // This is acceptable for a best-effort cache: correctness is preserved, only efficiency may be reduced.
        var value = await factory(ct).ConfigureAwait(false);
        Store(key, value, options);
        return value;
    }

    public async ValueTask<T> GetOrSetAsync<T>(
        string key, Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow
            && entry.Value is T typed)
            return typed;

        var factoryResult = await factory(ct).ConfigureAwait(false);
        if (factoryResult.ShouldCache)
            Store(key, factoryResult.Value, options);

        return factoryResult.Value;
    }

    public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
            return new ValueTask<T?>((T?)entry.Value);
        return new ValueTask<T?>(default(T?));
    }

    public ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
            return new ValueTask<(bool, T?)>((true, (T?)entry.Value));
        return new ValueTask<(bool, T?)>((false, default));
    }

    public ValueTask SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        Store(key, value, options);
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        Remove(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default)
    {
        InvalidateTag(tag);
        return ValueTask.CompletedTask;
    }

    public ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
    {
        foreach (var tag in tags)
            InvalidateTag(tag);
        return ValueTask.CompletedTask;
    }

    public void Dispose() => _cleanupTimer.Dispose();

    private void Store<T>(string key, T value, CacheEntryOptions? options)
    {
        var ttl = options?.Duration ?? TimeSpan.FromMinutes(5);
        var tags = options?.Tags ?? [];
        _cache[key] = new CacheEntry(value, DateTimeOffset.UtcNow.Add(ttl), tags);

        foreach (var tag in tags)
            _tagIndex.GetOrAdd(tag, _ => new ConcurrentDictionary<string, byte>())[key] = 0;
    }

    private void Remove(string key)
    {
        if (_cache.TryRemove(key, out var entry))
            UnindexTags(key, entry.Tags);
    }

    private void InvalidateTag(string tag)
    {
        if (!_tagIndex.TryRemove(tag, out var keys))
            return;

        foreach (var key in keys.Keys)
            if (_cache.TryRemove(key, out var entry))
                // Drop the key from its other tags too, so stale references do not accumulate.
                UnindexTags(key, entry.Tags.Remove(tag));
    }

    private void UnindexTags(string key, ImmutableArray<string> tags)
    {
        foreach (var tag in tags)
            if (_tagIndex.TryGetValue(tag, out var keys))
            {
                keys.TryRemove(key, out _);
                if (keys.IsEmpty)
                    _tagIndex.TryRemove(tag, out _);
            }
    }

    private void Cleanup()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _cache.Keys)
            if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt <= now && _cache.TryRemove(key, out var removed))
                UnindexTags(key, removed.Tags);
    }

    private sealed record CacheEntry(object? Value, DateTimeOffset ExpiresAt, ImmutableArray<string> Tags);
}
