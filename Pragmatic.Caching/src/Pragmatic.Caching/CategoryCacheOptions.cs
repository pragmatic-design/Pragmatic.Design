namespace Pragmatic.Caching;

/// <summary>
///     Per-category cache configuration. Applied when registering a category via
///     <c>CachingBuilder.ForCategory&lt;T&gt;()</c>.
/// </summary>
public sealed class CategoryCacheOptions
{
    /// <summary>
    ///     Key prefix for all entries in this category.
    ///     Isolates this category's keys from other categories in the same backend.
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    ///     Default cache duration for entries without explicit duration.
    /// </summary>
    public TimeSpan? DefaultDuration { get; set; }
}
