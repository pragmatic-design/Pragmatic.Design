namespace Pragmatic.Caching.Redis;

/// <summary>What a broadcast invalidation removes on the nodes that receive it.</summary>
public enum CacheInvalidationKind
{
    /// <summary>Every entry carrying the tag.</summary>
    Tag,

    /// <summary>The entry under the key, exactly as the stack passed it to <c>HybridCache</c>.</summary>
    Key,
}
