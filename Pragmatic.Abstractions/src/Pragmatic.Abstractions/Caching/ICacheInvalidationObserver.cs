// ReSharper disable once CheckNamespace — interface lives in Abstractions but namespace matches Pragmatic.Caching
namespace Pragmatic.Caching;

/// <summary>
///     Told about each invalidation another node broadcast, once this node has applied it to its own cache.
/// </summary>
/// <remarks>
///     <para>
///         For a cache this node keeps outside <c>HybridCache</c>, which a broadcast does not reach on its own: the in-memory lookup caches
///         above all, read synchronously by generated navigations (#763). The writer invalidates a tag through <see cref="ICacheStack" />,
///         which a distributed stack broadcasts; each other node's observers hear the tag and refresh what it names.
///     </para>
///     <para>
///         Called by <c>Pragmatic.Caching.Redis</c>'s subscriber, after the message is applied. Without a distributed invalidation
///         broadcast nothing calls it: an invalidation on this node reaches this node's caches directly.
///     </para>
/// </remarks>
public interface ICacheInvalidationObserver
{
    /// <summary>Another node invalidated <paramref name="tag" />.</summary>
    Task OnTagInvalidatedAsync(string tag, CancellationToken ct);

    /// <summary>Another node removed <paramref name="key" />.</summary>
    Task OnKeyInvalidatedAsync(string key, CancellationToken ct);
}
