using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Caching.Extensions;

/// <summary>
///     Extension methods for registering Pragmatic.Caching services.
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds Pragmatic.Caching services with default options.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Prerequisite:</b> <c>HybridCache</c> must already be registered before calling this method
        ///         (e.g. <c>services.AddHybridCache()</c>). Omitting this step will cause a runtime
        ///         <see cref="InvalidOperationException"/> when <see cref="ICacheStack"/> is first resolved.
        ///     </para>
        /// </remarks>
        public IServiceCollection AddPragmaticCaching()
        {
            return services.AddPragmaticCaching(static (CachingOptions _) => { });
        }

        /// <summary>
        ///     Adds Pragmatic.Caching services with the specified options.
        ///     Requires HybridCache to be registered (call AddHybridCache first).
        /// </summary>
        public IServiceCollection AddPragmaticCaching(Action<CachingOptions> configure)
        {
            ThrowIfNull(services);
            ThrowIfNull(configure);

            services.Configure(configure);
            services.TryAddSingleton<ICacheStack, HybridCacheStack>();

            // No categories on this overload, so the registry is empty and the resolver answers with
            // the default stack for every category — which is where those entries live.
            services.TryAddSingleton(CacheCategoryRegistry.Empty);
            services.TryAddSingleton<ICacheStackResolver, KeyedCacheStackResolver>();

            return services;
        }
    }

    /// <summary>
    ///     Adds Pragmatic.Caching services with per-category routing.
    ///     Requires HybridCache to be registered (call AddHybridCache first).
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
    public static IServiceCollection AddPragmaticCaching(
        this IServiceCollection services,
        Action<CachingBuilder> configure)
    {
        ThrowIfNull(services);
        ThrowIfNull(configure);

        var builder = new CachingBuilder();
        configure(builder);

        // Register global options
        services.Configure<CachingOptions>(o =>
        {
            o.DefaultDuration = builder.Options.DefaultDuration;
            o.EnableQueryCaching = builder.Options.EnableQueryCaching;
            o.EnableEventInvalidation = builder.Options.EnableEventInvalidation;
        });

        // Register default (unkeyed) ICacheStack
        services.TryAddSingleton<ICacheStack, HybridCacheStack>();

        // The categories, kept so that an invalidation naming none can still reach them: keyed
        // services cannot be enumerated from the container.
        services.TryAddSingleton(new CacheCategoryRegistry(
            [.. builder.Categories.Keys.Select(t => t.FullName).OfType<string>()]));
        services.TryAddSingleton<ICacheStackResolver, KeyedCacheStackResolver>();

        // Register category-specific keyed ICacheStack instances
        foreach (var (categoryType, categoryOptions) in builder.Categories)
        {
            var key = categoryType.FullName
                ?? throw new InvalidOperationException(
                    $"Cache category type '{categoryType}' has no FullName. " +
                    "Generic types and types defined in the global namespace cannot be used as cache categories.");
            services.AddKeyedSingleton<ICacheStack>(key, (sp, _) =>
            {
                var defaultStack = sp.GetRequiredService<ICacheStack>();
                return new PrefixedCacheStack(defaultStack, categoryOptions.KeyPrefix, categoryOptions.DefaultDuration);
            });
        }

        return services;
    }
}
