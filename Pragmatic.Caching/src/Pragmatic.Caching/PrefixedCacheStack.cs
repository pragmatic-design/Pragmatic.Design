using System.Collections.Immutable;

namespace Pragmatic.Caching;

/// <summary>
///     Wraps an <see cref="ICacheStack"/> adding a key prefix to all operations.
///     Used for category-based isolation — each category has its own prefix.
/// </summary>
/// <remarks>
///     Tags are also prefixed. SG-generated invalidators must use the same ICacheStack instance
///     (resolved via DI category) to ensure prefix consistency. Direct tag invalidation on
///     the underlying HybridCacheStack will NOT match prefixed tags.
/// </remarks>
internal sealed class PrefixedCacheStack(ICacheStack inner, string prefix, TimeSpan? defaultDuration) : ICacheStack
{
    public ValueTask<T> GetOrSetAsync<T>(
        string key, Func<CancellationToken, ValueTask<T>> factory,
        CacheEntryOptions? options = null, CancellationToken ct = default)
        => inner.GetOrSetAsync(prefix + key, factory, ApplyDefaults(options), ct);

    public ValueTask<T> GetOrSetAsync<T>(
        string key, Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions? options = null, CancellationToken ct = default)
        => inner.GetOrSetAsync(prefix + key, factory, ApplyDefaults(options), ct);

    public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
        => inner.GetAsync<T>(prefix + key, ct);

    public ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
        => inner.TryGetAsync<T>(prefix + key, ct);

    public ValueTask SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
        => inner.SetAsync(prefix + key, value, ApplyDefaults(options), ct);

    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
        => inner.RemoveAsync(prefix + key, ct);

    public ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default)
        => inner.InvalidateByTagAsync(PrefixTag(tag), ct);

    public ValueTask<long> IncrementAsync(string key, long delta, TimeSpan? ttl = null, CancellationToken ct = default)
        => inner.IncrementAsync(prefix + key, delta, ttl, ct);

    public ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
    {
        // Materialise into an array to avoid LINQ iterator allocation on each element access.
        var prefixed = tags switch
        {
            string[] arr => Array.ConvertAll(arr, PrefixTag),
            IList<string> list => PrefixList(list),
            _ => tags.Select(PrefixTag).ToArray()
        };
        return inner.InvalidateByTagsAsync(prefixed, ct);
    }

    private string[] PrefixList(IList<string> list)
    {
        var result = new string[list.Count];
        for (var i = 0; i < list.Count; i++)
            result[i] = PrefixTag(list[i]);
        return result;
    }

    // Always prefix the tag. Callers of this stack pass RAW (unprefixed) tags — this wrapper owns the
    // prefixing on both the write and the invalidation path, so they stay symmetric. A previous
    // StartsWith(prefix) short-circuit was unsound: a legitimate raw tag that happened to begin with the
    // prefix string was left unprefixed, so it never matched the stored (prefixed) entry — a silent
    // tag-invalidation miss. Each tag flows through here exactly once, so there is no double-prefixing.
    private string PrefixTag(string tag) => prefix + tag;

    private CacheEntryOptions? ApplyDefaults(CacheEntryOptions? options)
    {
        // Prefix entry tags at write time so they match InvalidateByTagAsync(prefixed).
        // Without this, a user invalidating "user:123" via this prefixed stack would
        // build key "cat:user:123" while the entry was stored with tag "user:123"
        // raw — the invalidation would miss every matching entry.
        var prefixedTags = options?.Tags.IsDefaultOrEmpty == false
            ? options.Tags.Select(PrefixTag).ToImmutableArray()
            : options?.Tags ?? default;

        // No category-default duration to apply: use the caller's options as-is.
        if (defaultDuration is null || options?.Duration is not null)
        {
            if (options is null)
                return null;

            // Tags already match — no need to allocate a new options object.
            if (options.Tags.Equals(prefixedTags))
                return options;

            return new CacheEntryOptions
            {
                Duration = options.Duration,
                SlidingDuration = options.SlidingDuration,
                Tags = prefixedTags,
                Priority = options.Priority
            };
        }

        // Apply category-default duration; merge caller's other options when present.
        if (options is not null)
        {
            return new CacheEntryOptions
            {
                Duration = defaultDuration,
                SlidingDuration = options.SlidingDuration,
                Tags = prefixedTags,
                Priority = options.Priority
            };
        }

        return CacheEntryOptions.WithDuration(defaultDuration.Value);
    }
}
