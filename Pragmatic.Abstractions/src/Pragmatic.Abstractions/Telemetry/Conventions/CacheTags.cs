namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     Tag names for caching operations.
/// </summary>
public static class CacheTags
{
    /// <summary>Whether the cache lookup was a hit (true) or miss (false).</summary>
    public const string Hit = "cache.hit";

    /// <summary>The cache key used for the operation.</summary>
    public const string Key = "pragmatic.cache.key";

    /// <summary>The cache operation type: "get", "set", "invalidate".</summary>
    public const string Operation = "pragmatic.cache.operation";

    /// <summary>Invalidation tags associated with the operation.</summary>
    public const string Tags = "pragmatic.cache.tags";

    /// <summary>The node that applied an invalidation another node broadcast.</summary>
    public const string Node = "pragmatic.cache.node";
}
