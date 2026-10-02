// ReSharper disable once CheckNamespace
namespace Pragmatic.Caching;

/// <summary>
///     Specifies the priority for cache eviction.
///     Higher priority items are less likely to be evicted under memory pressure.
/// </summary>
public enum CachePriority
{
    /// <summary>Lowest priority. First to be evicted.</summary>
    Low = 0,

    /// <summary>Default priority.</summary>
    Normal = 1,

    /// <summary>Higher priority. Less likely to be evicted.</summary>
    High = 2,

    /// <summary>Never automatically evicted. Use sparingly.</summary>
    NeverRemove = 3
}
