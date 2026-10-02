using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Caching.Redis;

/// <summary>
///     <see cref="ICacheStack" /> decorator that tells the other nodes about every invalidation it runs.
/// </summary>
/// <remarks>
///     <para>
///         The invalidation runs here first, then goes out on <see cref="RedisCacheInvalidationChannel" />;
///         <see cref="CacheInvalidationSubscriber" /> applies it on the other nodes. Reads and writes are
///         untouched — only what removes an entry is broadcast.
///     </para>
///     <para>
///         The keys published are the keys this stack receives, which are the keys the stack beneath it
///         passes to <c>HybridCache</c>: a category stack is a prefix over the default one, so a key
///         reaching this decorator already carries its category.
///     </para>
///     <para>
///         <see cref="InvalidateByTagsAsync" /> broadcasts every tag even when the local invalidation of
///         one failed: the other nodes' copies are no less stale for it, and the failure still reaches
///         the caller.
///     </para>
/// </remarks>
public sealed class BroadcastingCacheStack : ICacheStack
{
    private readonly ICacheStack _inner;
    private readonly RedisCacheInvalidationChannel _channel;

    /// <summary>Wraps <paramref name="inner" />, broadcasting its invalidations on <paramref name="channel" />.</summary>
    public BroadcastingCacheStack(ICacheStack inner, RedisCacheInvalidationChannel channel)
    {
        ThrowIfNull(inner);
        ThrowIfNull(channel);
        _inner = inner;
        _channel = channel;
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        await _inner.RemoveAsync(key, ct).ConfigureAwait(false);
        await _channel.PublishAsync(CacheInvalidationKind.Key, key).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default)
    {
        await _inner.InvalidateByTagAsync(tag, ct).ConfigureAwait(false);
        await _channel.PublishAsync(CacheInvalidationKind.Tag, tag).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>The same as <see cref="InvalidateByTagAsync" />, so a tag is broadcast once.</remarks>
    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
        => InvalidateByTagAsync(tag, ct);

    /// <inheritdoc />
    public async ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
    {
        ThrowIfNull(tags);
        var tagList = tags as IList<string> ?? tags.ToList();

        try
        {
            await _inner.InvalidateByTagsAsync(tagList, ct).ConfigureAwait(false);
        }
        finally
        {
            foreach (var tag in tagList)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                    await _channel.PublishAsync(CacheInvalidationKind.Tag, tag).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<T> GetOrSetAsync<T>(
        string key, Func<CancellationToken, ValueTask<T>> factory,
        CacheEntryOptions? options = null, CancellationToken ct = default)
        => _inner.GetOrSetAsync(key, factory, options, ct);

    /// <inheritdoc />
    public ValueTask<T> GetOrSetAsync<T>(
        string key, Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions? options = null, CancellationToken ct = default)
        => _inner.GetOrSetAsync(key, factory, options, ct);

    /// <inheritdoc />
    public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
        => _inner.GetAsync<T>(key, ct);

    /// <inheritdoc />
    public ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
        => _inner.TryGetAsync<T>(key, ct);

    /// <inheritdoc />
    public ValueTask SetAsync<T>(
        string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
        => _inner.SetAsync(key, value, options, ct);

    /// <inheritdoc />
    public ValueTask<long> IncrementAsync(
        string key, long delta, TimeSpan? ttl = null, CancellationToken ct = default)
        => _inner.IncrementAsync(key, delta, ttl, ct);
}
