using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Caching.Diagnostics;
using Pragmatic.Telemetry;
using static Pragmatic.Ensure.Ensure;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Caching;

/// <summary>
///     ICacheStack implementation backed by Microsoft.Extensions.Caching.Hybrid.
///     Provides L1 (memory) + L2 (distributed) caching with stampede protection.
/// </summary>
public sealed partial class HybridCacheStack : ICacheStack
{
    private readonly HybridCache _cache;
    private readonly ILogger<HybridCacheStack> _logger;
    private readonly TimeSpan? _defaultDuration;

    // Per-key gates serialising the read-modify-write inside IncrementAsync so concurrent
    // increments on the same key cannot interleave (single-instance atomicity). Distinct keys
    // never contend. Entries are pruned once their gate is uncontended to keep the map bounded.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _incrementGates = new(StringComparer.Ordinal);

    // Constant, and on the read path: GetAsync delegates to TryGetAsync and IncrementAsync delegates
    // to GetAsync, so building it per call allocated once for every read. Writes are disabled so a
    // miss never stores the default(T) the probe factory returns.
    private static readonly HybridCacheEntryOptions NoWriteOptions = new()
    {
        Flags = HybridCacheEntryFlags.DisableLocalCacheWrite
                | HybridCacheEntryFlags.DisableDistributedCacheWrite
    };

    /// <summary>
    ///     Creates a new HybridCacheStack wrapping the provided HybridCache.
    /// </summary>
    /// <param name="cache">The underlying HybridCache instance.</param>
    /// <param name="logger">Optional logger. When null, logging is suppressed.</param>
    /// <param name="options">
    ///     Optional caching options. When provided, <see cref="CachingOptions.DefaultDuration"/> is applied
    ///     as the expiration for entries whose <see cref="CacheEntryOptions"/> specify no duration.
    /// </param>
    public HybridCacheStack(HybridCache cache, ILogger<HybridCacheStack>? logger = null,
        IOptions<CachingOptions>? options = null)
    {
        ThrowIfNull(cache);
        _cache = cache;
        _logger = logger ?? NullLogger<HybridCacheStack>.Instance;
        _defaultDuration = options?.Value.DefaultDuration;
    }

    /// <inheritdoc />
    public async ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CacheEntryOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfNullOrWhiteSpace(key);
        ThrowIfNull(factory);

        using var activity = CachingDiagnostics.ActivitySource.StartActivity("Cache.GetOrSet");
        activity?.SetTag(CacheTags.Key, key);
        activity?.SetTag(CacheTags.Operation, "get_or_set");

        LogGetOrSet(key);

        var entryOptions = ToHybridCacheEntryOptions(options);
        var tags = options?.Tags.IsDefaultOrEmpty != false ? null : options.Tags.AsSpan().ToArray();

        // Use a box so the factory communicates hit/miss via a reference rather than
        // mutating a captured local — this avoids any issue if HybridCache ever invokes
        // the factory concurrently across stampede windows.
        var missBox = new StrongBox<bool>();
        var result = await _cache.GetOrCreateAsync(
            key,
            async (ct2) =>
            {
                missBox.Value = true;
                return await factory(ct2).ConfigureAwait(false);
            },
            entryOptions,
            tags,
            ct).ConfigureAwait(false);

        if (missBox.Value)
            CachingDiagnostics.CacheMisses.Add(1);
        else
            CachingDiagnostics.CacheHits.Add(1);

        activity?.SetSuccess();
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfNullOrWhiteSpace(key);
        ThrowIfNull(factory);

        using var activity = CachingDiagnostics.ActivitySource.StartActivity("Cache.GetOrSet");
        activity?.SetTag(CacheTags.Key, key);
        activity?.SetTag(CacheTags.Operation, "get_or_set");

        LogGetOrSet(key);

        var entryOptions = ToHybridCacheEntryOptions(options);
        var tags = options?.Tags.IsDefaultOrEmpty != false ? null : options.Tags.AsSpan().ToArray();

        // Use a box so the factory communicates hit/miss + result via a reference rather than
        // mutating captured locals — safe if HybridCache ever invokes the factory concurrently.
        var stateBox = new StrongBox<(bool WasMiss, CacheFactoryResult<T> FactoryResult)>();

        var result = await _cache.GetOrCreateAsync(
            key,
            async ct2 =>
            {
                var fr = await factory(ct2).ConfigureAwait(false);
                stateBox.Value = (true, fr);
                return fr.Value;
            },
            entryOptions,
            tags,
            ct).ConfigureAwait(false);

        if (stateBox.Value.WasMiss)
        {
            CachingDiagnostics.CacheMisses.Add(1);

            // Factory signalled the value should not be cached — evict it. HybridCache has already
            // written it by the time GetOrCreateAsync returns, and ShouldCache is only known once the
            // factory ran, so the write cannot be suppressed in advance: a concurrent reader in this
            // window sees the value, and a crash here leaves it until it expires.
            if (!stateBox.Value.FactoryResult.ShouldCache)
                await _cache.RemoveAsync(key, ct).ConfigureAwait(false);
        }
        else
        {
            CachingDiagnostics.CacheHits.Add(1);
        }

        activity?.SetSuccess();
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ThrowIfNullOrWhiteSpace(key);

        var (found, value) = await TryGetAsync<T>(key, ct).ConfigureAwait(false);
        return found ? value : default;
    }

    /// <inheritdoc />
    public async ValueTask SetAsync<T>(
        string key,
        T value,
        CacheEntryOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfNullOrWhiteSpace(key);

        using var activity = CachingDiagnostics.ActivitySource.StartActivity("Cache.Set");
        activity?.SetTag(CacheTags.Key, key);
        activity?.SetTag(CacheTags.Operation, "set");

        CachingDiagnostics.CacheSets.Add(1);
        LogCacheSet(key);

        var entryOptions = ToHybridCacheEntryOptions(options);
        var tags = options?.Tags.IsDefaultOrEmpty != false ? null : options.Tags.AsSpan().ToArray();

        await _cache.SetAsync(key, value, entryOptions, tags, ct).ConfigureAwait(false);
        activity?.SetSuccess();
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        ThrowIfNullOrWhiteSpace(key);

        using var activity = CachingDiagnostics.ActivitySource.StartActivity("Cache.Remove");
        activity?.SetTag(CacheTags.Key, key);
        activity?.SetTag(CacheTags.Operation, "invalidate");

        CachingDiagnostics.CacheInvalidations.Add(1);
        LogCacheRemove(key);
        await _cache.RemoveAsync(key, ct).ConfigureAwait(false);
        activity?.SetSuccess();
    }

    /// <inheritdoc />
    public async ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default)
    {
        ThrowIfNullOrWhiteSpace(tag);

        using var activity = CachingDiagnostics.ActivitySource.StartActivity("Cache.InvalidateByTag");
        activity?.SetTag(CacheTags.Tags, tag);
        activity?.SetTag(CacheTags.Operation, "invalidate");

        CachingDiagnostics.CacheInvalidations.Add(1);
        LogInvalidateTag(tag);
        await _cache.RemoveByTagAsync(tag, ct).ConfigureAwait(false);
        activity?.SetSuccess();
    }

    /// <inheritdoc />
    public async ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
    {
        ThrowIfNull(tags);

        var tagList = tags as IList<string> ?? tags.ToList();

        using var activity = CachingDiagnostics.ActivitySource.StartActivity("Cache.InvalidateByTags");
        activity?.SetTag(CacheTags.Tags, string.Join(",", tagList));
        activity?.SetTag(CacheTags.Operation, "invalidate");

        LogInvalidateTags(tagList.Count);

        List<Exception>? failures = null;
        foreach (var tag in tagList)
        {
            if (string.IsNullOrWhiteSpace(tag))
                continue;

            try
            {
                await _cache.RemoveByTagAsync(tag, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // One failing tag must not stop the others: a caller invalidating after a write wants
                // as much of the cache cleared as can be. But it must not be told the cache is clean
                // when it is not — the whole point of calling this is that stale entries are gone, and
                // a caller that believes a failed invalidation serves stale data until the entry
                // expires. Collected here, thrown below.
                (failures ??= []).Add(ex);
                LogTagInvalidationFailed(tag, ex);
            }
        }

        if (failures is null)
        {
            activity?.SetSuccess();
            return;
        }

        activity?.SetFailure("cache.partial_invalidation_failure",
            $"{failures.Count} of {tagList.Count} tag(s) failed to invalidate.");

        throw new AggregateException(
            $"{failures.Count} of {tagList.Count} tag(s) failed to invalidate; the entries they cover "
            + "may still be served. The remaining tags were invalidated.", failures);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Uses <see cref="HybridCacheEntryFlags.DisableLocalCacheWrite"/> and
    ///     <see cref="HybridCacheEntryFlags.DisableDistributedCacheWrite"/> so that when the factory
    ///     fires (cache miss), the default value is never persisted to any cache tier.
    /// </remarks>
    public async ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
    {
        ThrowIfNullOrWhiteSpace(key);

        using var activity = CachingDiagnostics.ActivitySource.StartActivity("Cache.TryGet");
        activity?.SetTag(CacheTags.Key, key);
        activity?.SetTag(CacheTags.Operation, "get");

        // Use a box so the factory communicates hit/miss via a reference rather than
        // mutating a captured local — safe if HybridCache ever invokes the factory concurrently.
        var missBox = new StrongBox<bool>();

        var value = await _cache.GetOrCreateAsync(
            key,
            _ =>
            {
                missBox.Value = true;
                return ValueTask.FromResult(default(T)!);
            },
            NoWriteOptions,
            cancellationToken: ct).ConfigureAwait(false);

        if (missBox.Value)
        {
            CachingDiagnostics.CacheMisses.Add(1);
            activity?.SetTag(CacheTags.Hit, false);
            LogCacheMiss(key);
        }
        else
        {
            CachingDiagnostics.CacheHits.Add(1);
            activity?.SetTag(CacheTags.Hit, true);
            LogCacheHit(key);
        }

        activity?.SetSuccess();
        return (Found: !missBox.Value, Value: missBox.Value ? default : value);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Atomic per key within this process: the read-modify-write runs under a per-key
    ///     <see cref="SemaphoreSlim"/> so concurrent increments on the same key are serialised.
    ///     This does NOT provide cross-instance atomicity — see <see cref="ICacheStack.IncrementAsync"/>.
    /// </remarks>
    public async ValueTask<long> IncrementAsync(
        string key, long delta, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ThrowIfNullOrWhiteSpace(key);

        using var activity = CachingDiagnostics.ActivitySource.StartActivity("Cache.Increment");
        activity?.SetTag(CacheTags.Key, key);
        activity?.SetTag(CacheTags.Operation, "increment");

        // Acquire, then confirm the gate we hold is still the one this key maps to. Between another
        // caller's Release and its prune there is a window in which we can already hold a reference to a
        // gate that is about to be unmapped: we would then serialise on it while the next arrival, finding
        // the key absent, creates a second gate and serialises on that one. Two critical sections, one
        // lost update. The prune can only succeed while the gate is unheld, so a gate we hold and have
        // confirmed cannot be unmapped underneath us.
        SemaphoreSlim gate;
        while (true)
        {
            gate = _incrementGates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct).ConfigureAwait(false);

            if (_incrementGates.TryGetValue(key, out var mapped) && ReferenceEquals(mapped, gate))
                break;

            gate.Release();
        }

        try
        {
            var current = await GetAsync<long>(key, ct).ConfigureAwait(false);
            var next = current + delta;

            var options = ttl is { } d ? CacheEntryOptions.WithDuration(d) : null;
            await SetAsync(key, next, options, ct).ConfigureAwait(false);

            LogCacheIncrement(key, delta, next);
            activity?.SetSuccess();
            return next;
        }
        finally
        {
            gate.Release();
            // Best-effort prune so the map does not grow unbounded across many distinct one-off keys
            // (e.g. per-window rate-limit keys). Re-acquire non-blockingly: success means nobody holds the
            // gate right now. That is NOT the same as nobody being about to take it — a caller may already
            // hold the reference without having awaited yet — which is why acquisition above re-checks the
            // mapping rather than trusting this. The gate is NOT disposed —
            // SemaphoreSlim owns no unmanaged resource unless AvailableWaitHandle is used (it isn't), and
            // disposing a gate another thread might still hold or acquire is exactly what raced into
            // ObjectDisposedException on Release/Wait under concurrency. Not disposing keeps every
            // reference safe; the GC reclaims the removed gate.
            if (gate.Wait(0))
            {
                _incrementGates.TryRemove(new KeyValuePair<string, SemaphoreSlim>(key, gate));
                gate.Release();
            }
        }
    }

    private HybridCacheEntryOptions? ToHybridCacheEntryOptions(CacheEntryOptions? options)
    {
        // Apply the configured DefaultDuration when the entry itself specifies no duration, so
        // CachingOptions.DefaultDuration is honoured instead of falling through to HybridCache's own default.
        var duration = options?.Duration ?? _defaultDuration;
        var sliding = options?.SlidingDuration;

        if (duration is null && sliding is null)
            return null;

        // HybridCache has no sliding expiration: SlidingDuration is approximated as the L1
        // (local) absolute expiration — a shorter local TTL, NOT an access-refreshed window.
        // Documented on CacheEntryOptions.SlidingDuration.
        return new HybridCacheEntryOptions
        {
            Expiration = duration,
            LocalCacheExpiration = sliding ?? duration
        };
    }
}
