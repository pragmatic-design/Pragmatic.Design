namespace Pragmatic.Logging.Context;

/// <summary>
/// Configuration options for ContextManager.
/// </summary>
public sealed class ContextManagerOptions
{
    /// <summary>Gets or sets whether to register default system providers.</summary>
    public bool EnableDefaultProviders { get; set; } = true;

    /// <summary>Gets or sets the cache timeout duration.</summary>
    public TimeSpan CacheTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets or sets whether to collect performance metrics.</summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>Gets or sets the maximum number of async context operations to cache.</summary>
    public int MaxAsyncCacheSize { get; set; } = 1000;
}