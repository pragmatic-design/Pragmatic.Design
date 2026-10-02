namespace Pragmatic.Caching;

/// <summary>
///     Configuration options for Pragmatic.Caching.
/// </summary>
public sealed class CachingOptions
{
    /// <summary>
    ///     Default cache duration when not specified. Default is 5 minutes.
    /// </summary>
    public TimeSpan DefaultDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     When true, enables automatic caching for types marked with [Cacheable].
    ///     Default is true.
    /// </summary>
    public bool EnableQueryCaching { get; set; } = true;

    /// <summary>
    ///     When true, enables automatic invalidation via [InvalidatesCache] event handlers.
    ///     Default is true.
    /// </summary>
    public bool EnableEventInvalidation { get; set; } = true;
}
