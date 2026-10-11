using Microsoft.Extensions.Caching.Hybrid;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     A real HybridCache that refuses to invalidate one tag, as a distributed tier that has gone away
///     does: the way to reach the stack's partial-invalidation path.
/// </summary>
internal sealed class TagRefusingHybridCache(HybridCache inner, string refusedTag) : HybridCache
{
    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
        => inner.GetOrCreateAsync(key, state, factory, options, tags, cancellationToken);

    public override ValueTask SetAsync<T>(
        string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
        => inner.SetAsync(key, value, options, tags, cancellationToken);

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        => inner.RemoveAsync(key, cancellationToken);

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        => tag == refusedTag
            ? throw new InvalidOperationException($"The distributed tier refused tag '{tag}'")
            : inner.RemoveByTagAsync(tag, cancellationToken);
}
