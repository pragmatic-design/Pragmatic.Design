using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Caching;

/// <summary>
///     Fluent builder for configuring Pragmatic.Caching with per-category routing.
/// </summary>
/// <example>
///     <code>
/// services.AddPragmaticCaching(cache =>
/// {
///     cache.WithDefaultOptions(o => o.DefaultDuration = TimeSpan.FromMinutes(10));
///     cache.ForCategory&lt;CacheCategories.OutputCache&gt;(o => o.KeyPrefix = "oc:");
///     cache.ForCategory&lt;CacheCategories.Permissions&gt;(o =>
///     {
///         o.KeyPrefix = "perms:";
///         o.DefaultDuration = TimeSpan.FromMinutes(5);
///     });
/// });
///     </code>
/// </example>
public sealed class CachingBuilder
{
    internal CachingOptions Options { get; } = new();
    internal Dictionary<Type, CategoryCacheOptions> Categories { get; } = new();

    internal CachingBuilder() { }

    /// <summary>
    ///     Configures default caching options (applied to all categories without specific config).
    /// </summary>
    public CachingBuilder WithDefaultOptions(Action<CachingOptions> configure)
    {
        ThrowIfNull(configure);
        configure(Options);
        return this;
    }

    /// <summary>
    ///     Registers a category-specific cache configuration.
    ///     The category <typeparamref name="TCategory"/> is a marker type from
    ///     <see cref="CacheCategories"/> or a custom application type.
    /// </summary>
    /// <typeparam name="TCategory">Marker type for the cache category.</typeparam>
    /// <param name="configure">Configuration for this category.</param>
    public CachingBuilder ForCategory<TCategory>(Action<CategoryCacheOptions> configure)
    {
        ThrowIfNull(configure);

        var options = new CategoryCacheOptions();
        configure(options);

        // Auto-generate prefix from category type name if not set
        if (string.IsNullOrEmpty(options.KeyPrefix))
            options.KeyPrefix = typeof(TCategory).Name.ToLowerInvariant() + ":";

        Categories[typeof(TCategory)] = options;
        return this;
    }
}
