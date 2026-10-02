using StackExchange.Redis;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Caching.Redis;

/// <summary>
///     <see cref="ICacheStack"/> decorator that makes <see cref="IncrementAsync"/> atomic ACROSS
///     instances via Redis (a single Lua <c>INCRBY</c>+<c>PEXPIRE</c> script). Reads and ordinary
///     writes keep their existing backend; only counters (rate limiting, quotas) move to Redis.
///     ⚠️ <see cref="RemoveAsync"/> is the one member that touches <b>both</b>, because a key does not
///     say which of the two wrote it — see the remark there.
/// </summary>
/// <remarks>
///     <para>
///         The default <c>HybridCacheStack.IncrementAsync</c> is atomic only per process: two
///         instances incrementing the same counter race on a read-modify-write and over-admit.
///         With this decorator the increment executes server-side in Redis, so a multi-node
///         deployment enforces limits strictly (see <c>PragmaticDistributedRateLimiter</c>).
///     </para>
///     <para>
///         <b>Cancellation:</b> the token is honoured before the call is issued and not
///         after. <c>ScriptEvaluateAsync</c> takes no <see cref="CancellationToken"/> in
///         StackExchange.Redis, so a cancellation during a stalled Redis call does not interrupt it;
///         the multiplexer's own timeouts bound the wait.
///     </para>
///     <para>
///         <b>Failure semantics:</b> Redis errors propagate — no silent fallback to the in-process
///         counter, which would quietly weaken a rate limit to per-instance enforcement. Callers
///         that prefer availability over strictness must catch and decide (the endpoints rate
///         limiter fails closed).
///     </para>
/// </remarks>
public sealed class RedisCounterCacheStack : ICacheStack
{
    // KEYS[1] counter key · ARGV[1] delta · ARGV[2] ttl in ms (-1 = don't touch expiry).
    // PEXPIRE is applied only when the increment CREATED the key (result == delta), so a
    // decrement (giving permits back) or a subsequent increment never extends the window.
    private const string IncrementScript = """
        local v = redis.call('INCRBY', KEYS[1], ARGV[1])
        if tonumber(ARGV[2]) > 0 and v == tonumber(ARGV[1]) then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        return v
        """;

    private readonly ICacheStack _inner;
    private readonly IConnectionMultiplexer _redis;

    /// <summary>Wraps <paramref name="inner"/>, routing atomic increments to <paramref name="redis"/>.</summary>
    public RedisCounterCacheStack(ICacheStack inner, IConnectionMultiplexer redis)
    {
        ThrowIfNull(inner);
        ThrowIfNull(redis);
        _inner = inner;
        _redis = redis;
    }

    /// <inheritdoc />
    public async ValueTask<long> IncrementAsync(
        string key, long delta, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        // Round a positive TTL up to at least 1ms so a sub-millisecond window is not truncated to 0,
        // which the Lua script treats as "no expiry" — leaving the counter key permanent. A null or
        // non-positive TTL maps to -1 (no PEXPIRE).
        var ttlMs = ttl is { } t && t > TimeSpan.Zero
            ? Math.Max(1L, (long)Math.Ceiling(t.TotalMilliseconds))
            : -1L;

        var db = _redis.GetDatabase();
        var result = await db.ScriptEvaluateAsync(
            IncrementScript,
            [(RedisKey)key],
            [delta, ttlMs]).ConfigureAwait(false);

        return (long)result;
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
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Both backends, because a key does not say which of them wrote it.</b> This
    ///         decorator splits by <em>operation</em>: <see cref="IncrementAsync" /> writes to Redis and
    ///         every other write goes to the inner stack. A remove that delegated only inwards left a
    ///         counter standing in Redis — and once a caller used a counter as a <b>reservation</b>, a
    ///         release that removed nothing meant its retry was refused for the reservation's whole
    ///         TTL, an hour by default.
    ///     </para>
    ///     <para>
    ///         Routing it only to Redis would be the same defect mirrored: an entry written by
    ///         <see cref="SetAsync" /> lives inside and would stop being removable. Removing a key that
    ///         is not there is a no-op in both, so doing both is the only version that is right for
    ///         every caller.
    ///     </para>
    /// </remarks>
    public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        ThrowIfNullOrWhiteSpace(key);

        await _inner.RemoveAsync(key, ct).ConfigureAwait(false);
        await _redis.GetDatabase().KeyDeleteAsync(key).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default)
        => _inner.InvalidateByTagAsync(tag, ct);

    /// <inheritdoc />
    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
        => _inner.RemoveByTagAsync(tag, ct);

    /// <inheritdoc />
    public ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
        => _inner.InvalidateByTagsAsync(tags, ct);
}
