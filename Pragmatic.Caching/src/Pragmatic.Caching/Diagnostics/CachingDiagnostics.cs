using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Caching.Diagnostics;

/// <summary>
///     Centralized diagnostics for Pragmatic.Caching: ActivitySource and Meter with instruments.
/// </summary>
public static class CachingDiagnostics
{
    /// <summary>The source name for all Caching activities.</summary>
    public const string SourceName = "Pragmatic.Caching";

    /// <summary>The ActivitySource for distributed tracing.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");

    /// <summary>The Meter for metrics collection.</summary>
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    /// <summary>Counter of cache hits.</summary>
    public static readonly Counter<long> CacheHits = Meter.CreateCounter<long>(
        "pragmatic.cache.hits",
        unit: "{operations}",
        description: "Total cache hits");

    /// <summary>Counter of cache misses.</summary>
    public static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>(
        "pragmatic.cache.misses",
        unit: "{operations}",
        description: "Total cache misses");

    /// <summary>Counter of cache set operations.</summary>
    public static readonly Counter<long> CacheSets = Meter.CreateCounter<long>(
        "pragmatic.cache.sets",
        unit: "{operations}",
        description: "Total cache set operations");

    /// <summary>Counter of cache invalidations (remove + tag invalidation).</summary>
    public static readonly Counter<long> CacheInvalidations = Meter.CreateCounter<long>(
        "pragmatic.cache.invalidations",
        unit: "{operations}",
        description: "Total cache invalidations");
}
