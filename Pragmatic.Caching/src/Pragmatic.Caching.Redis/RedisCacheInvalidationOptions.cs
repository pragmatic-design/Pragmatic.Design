namespace Pragmatic.Caching.Redis;

/// <summary>Options for <see cref="RedisCacheInvalidationChannel" />.</summary>
public sealed class RedisCacheInvalidationOptions
{
    /// <summary>
    ///     The Redis pub/sub channel the nodes of one application share. Default:
    ///     <c>pragmatic:cache:invalidations</c>.
    /// </summary>
    /// <remarks>
    ///     Two applications on one Redis with the default channel evict each other's entries under the
    ///     same tag or key — extra misses, never a wrong answer. Give each its own channel when that
    ///     matters.
    /// </remarks>
    public string Channel { get; set; } = "pragmatic:cache:invalidations";
}
