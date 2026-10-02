// ReSharper disable once CheckNamespace
using System.Collections.Immutable;

namespace Pragmatic.Caching;

/// <summary>
///     Options for a cache entry including duration, tags, and eviction priority.
/// </summary>
public sealed class CacheEntryOptions
{
    /// <summary>
    ///     Absolute expiration duration. After this time, the entry is removed.
    ///     <para>
    ///         Do not set both <see cref="Duration"/> and <see cref="SlidingDuration"/> on the
    ///         same entry. When both are set, behaviour is provider-defined; most implementations
    ///         treat the absolute expiry as an outer bound and the sliding window as inner bound,
    ///         but this is not guaranteed. Use one or the other to avoid subtle caching bugs.
    ///     </para>
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    ///     Sliding expiration duration. The entry is removed if not accessed within this time.
    ///     See <see cref="Duration"/> for the mutual-exclusion guidance.
    ///     <para>
    ///         <b>HybridCache backend caveat:</b> HybridCache has no true sliding expiration. The
    ///         default <c>HybridCacheStack</c> maps this value to the L1 (local) cache expiration —
    ///         an absolute per-tier bound, not an access-refreshed window. Entries therefore expire
    ///         at most <see cref="SlidingDuration"/> after being written locally, regardless of access.
    ///     </para>
    /// </summary>
    public TimeSpan? SlidingDuration { get; init; }

    /// <summary>
    ///     Tags for group invalidation. All entries with a matching tag can be invalidated together.
    /// </summary>
    public ImmutableArray<string> Tags { get; init; } = [];

    /// <summary>
    ///     Eviction priority. Higher priority items are less likely to be evicted under memory pressure.
    /// </summary>
    /// <remarks>
    ///     Not honored by the default HybridCache-backed stack (HybridCache exposes no priority
    ///     concept). It is advisory metadata that a custom <see cref="ICacheStack"/> implementation
    ///     may choose to apply.
    /// </remarks>
    public CachePriority Priority { get; init; } = CachePriority.Normal;

    /// <summary>Default options with 5 minute duration.</summary>
    public static CacheEntryOptions Default { get; } = new() { Duration = TimeSpan.FromMinutes(5) };

    /// <summary>Creates options with the specified absolute duration.</summary>
    /// <param name="duration">Absolute expiration duration after which the entry is removed.</param>
    /// <returns>Options configured with the given absolute <see cref="Duration"/>.</returns>
    public static CacheEntryOptions WithDuration(TimeSpan duration) => new() { Duration = duration };

    /// <summary>Creates options with sliding expiration.</summary>
    /// <param name="slidingDuration">Sliding window after which the entry is removed if not accessed.</param>
    /// <returns>Options configured with the given <see cref="SlidingDuration"/>.</returns>
    public static CacheEntryOptions WithSliding(TimeSpan slidingDuration) => new() { SlidingDuration = slidingDuration };
}
