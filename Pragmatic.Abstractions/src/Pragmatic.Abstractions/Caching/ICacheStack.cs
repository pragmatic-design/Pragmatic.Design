// ReSharper disable once CheckNamespace — interface lives in Abstractions but namespace matches Pragmatic.Caching
namespace Pragmatic.Caching;

/// <summary>
///     Unified caching abstraction. Provides get/set/remove with stampede protection,
///     tag-based invalidation, and optional category routing.
/// </summary>
/// <remarks>
///     <para>
///         Default implementation: <c>HybridCacheStack</c> in <c>Pragmatic.Caching</c>
///         (L1 memory + L2 distributed via <c>Microsoft.Extensions.Caching.Hybrid</c>).
///     </para>
///     <para>
///         Register with <c>AddPragmaticCaching()</c>. For category-specific backends,
///         use <c>ForCategory&lt;T&gt;()</c> on the <c>CachingBuilder</c>.
///     </para>
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Singleton)]
public interface ICacheStack
{
    /// <summary>
    ///     Gets or creates a cached value using the factory when cache miss occurs.
    ///     Includes stampede protection — concurrent requests share a single factory call.
    /// </summary>
    ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CacheEntryOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Gets or creates a cached value, with the factory controlling whether to cache the result.
    ///     When <see cref="CacheFactoryResult{T}.ShouldCache"/> is <c>false</c>, the value is returned
    ///     to the caller but not stored in the cache.
    /// </summary>
    ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Gets a cached value if it exists.
    /// </summary>
    ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    ///     Tries to get a cached value, distinguishing between cache miss and cached null/default.
    /// </summary>
    ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    ///     Sets a cached value explicitly.
    /// </summary>
    ValueTask SetAsync<T>(
        string key,
        T value,
        CacheEntryOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Removes a cached value by key.
    /// </summary>
    ValueTask RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    ///     Removes all cached values with the specified tag.
    /// </summary>
    ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default);

    /// <summary>
    ///     Removes all cached values with the specified tag.
    ///     Synonym for <see cref="InvalidateByTagAsync"/> with naming aligned to the
    ///     <c>Remove*</c> family.
    /// </summary>
    /// <remarks>
    ///     Default implementation delegates to <see cref="InvalidateByTagAsync"/>.
    /// </remarks>
    ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
        => InvalidateByTagAsync(tag, ct);

    /// <summary>
    ///     Removes all cached values with any of the specified tags.
    /// </summary>
    /// <remarks>
    ///     Every tag is attempted: one failure does not stop the rest. If any failed, an
    ///     <see cref="AggregateException"/> is thrown once they have all been tried, because a caller
    ///     that is told the cache is clean when it is not will serve stale data until the entries
    ///     expire. <see cref="InvalidateByTagAsync"/> propagates its single failure directly.
    /// </remarks>
    /// <exception cref="AggregateException">One or more tags could not be invalidated.</exception>
    ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default);

    /// <summary>
    ///     Atomically adds <paramref name="delta"/> to the numeric counter stored at
    ///     <paramref name="key"/> and returns the new value. When the entry does not yet
    ///     exist it is treated as <c>0</c> before adding <paramref name="delta"/>.
    /// </summary>
    /// <param name="key">The counter key.</param>
    /// <param name="delta">The amount to add (may be negative to decrement).</param>
    /// <param name="ttl">
    ///     When provided, the entry's time-to-live is (re)set to this value. When <c>null</c>
    ///     the implementation's default lifetime applies. Pass the rate-limit window here to
    ///     get fixed-window expiry semantics.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The counter value after adding <paramref name="delta"/>.</returns>
    /// <remarks>
    ///     <para>
    ///         The in-process implementation (<c>HybridCacheStack</c>) is <b>atomic per key</b>:
    ///         concurrent increments on the same key cannot interleave, so this is correct for a
    ///         single-instance deployment — the common rate-limiter over-admit case.
    ///     </para>
    ///     <para>
    ///         <b>Cross-instance atomicity is NOT guaranteed</b> by the default interface fallback
    ///         (a non-atomic read-modify-write) nor by the in-process stack, which only serialises
    ///         within one process. A distributed deployment that needs strict cross-instance
    ///         enforcement must register an <see cref="ICacheStack"/> backed by a store with a
    ///         native atomic increment (e.g. Redis <c>INCR</c>) and override this method accordingly.
    ///     </para>
    /// </remarks>
    ValueTask<long> IncrementAsync(string key, long delta, TimeSpan? ttl = null, CancellationToken ct = default)
        => IncrementFallbackAsync(key, delta, ttl, ct);

    /// <summary>
    ///     Default <see cref="IncrementAsync"/> implementation: a NON-atomic read-modify-write
    ///     usable by any backend. Implementations with a native atomic increment should override
    ///     <see cref="IncrementAsync"/> and ignore this helper.
    /// </summary>
    private async ValueTask<long> IncrementFallbackAsync(
        string key, long delta, TimeSpan? ttl, CancellationToken ct)
    {
        var current = await GetAsync<long>(key, ct).ConfigureAwait(false);
        var next = current + delta;
        var options = ttl is { } d ? CacheEntryOptions.WithDuration(d) : null;
        await SetAsync(key, next, options, ct).ConfigureAwait(false);
        return next;
    }
}
