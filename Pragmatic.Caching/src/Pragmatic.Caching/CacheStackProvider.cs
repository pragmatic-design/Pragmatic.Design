using Microsoft.Extensions.DependencyInjection;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Caching;

/// <summary>
///     Helper for resolving <see cref="ICacheStack"/> by category with fallback to the default.
/// </summary>
/// <remarks>
///     <para>
///         Categories are resolved using .NET 8+ keyed services.
///         The service key is <c>typeof(TCategory).FullName</c>.
///         If no category-specific registration exists, falls back to the unkeyed default.
///     </para>
/// </remarks>
public static class CacheStackProvider
{
    /// <summary>
    ///     Resolves the <see cref="ICacheStack"/> for the given category.
    ///     Falls back to the default (unkeyed) <see cref="ICacheStack"/> if no category-specific registration exists.
    /// </summary>
    /// <typeparam name="TCategory">Marker type for the cache category (from <see cref="CacheCategories"/>).</typeparam>
    /// <param name="serviceProvider">The service provider.</param>
    /// <returns>The category-specific or default ICacheStack.</returns>
    public static ICacheStack ForCategory<TCategory>(IServiceProvider serviceProvider)
    {
        ThrowIfNull(serviceProvider);
        var key = typeof(TCategory).FullName;
        if (key is null)
            return serviceProvider.GetRequiredService<ICacheStack>();
        return serviceProvider.GetKeyedService<ICacheStack>(key)
               ?? serviceProvider.GetRequiredService<ICacheStack>();
    }

    /// <summary>
    ///     Resolves the <see cref="ICacheStack"/> for the given category type.
    ///     Falls back to the default (unkeyed) <see cref="ICacheStack"/> if no category-specific registration exists.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="categoryType">The category marker type.</param>
    /// <returns>The category-specific or default ICacheStack.</returns>
    public static ICacheStack ForCategory(IServiceProvider serviceProvider, Type categoryType)
    {
        ThrowIfNull(serviceProvider);
        ThrowIfNull(categoryType);
        var key = categoryType.FullName;
        if (key is null)
            return serviceProvider.GetRequiredService<ICacheStack>();
        return serviceProvider.GetKeyedService<ICacheStack>(key)
               ?? serviceProvider.GetRequiredService<ICacheStack>();
    }

    /// <summary>
    ///     Resolves the <see cref="ICacheStack"/> for the given category without throwing.
    ///     Returns the category-specific stack, then the default (unkeyed) stack, or
    ///     <c>null</c> when no <see cref="ICacheStack"/> is registered at all.
    /// </summary>
    /// <remarks>
    ///     Use this from components that must degrade gracefully (no-op) when
    ///     Pragmatic.Caching is not registered, e.g. the ASP.NET Core output-cache store.
    /// </remarks>
    /// <typeparam name="TCategory">Marker type for the cache category (from <see cref="CacheCategories"/>).</typeparam>
    /// <param name="serviceProvider">The service provider.</param>
    /// <returns>The category-specific or default ICacheStack, or <c>null</c> if none is registered.</returns>
    public static ICacheStack? ForCategoryOrNull<TCategory>(IServiceProvider serviceProvider)
    {
        ThrowIfNull(serviceProvider);
        var key = typeof(TCategory).FullName;
        if (key is null)
            return serviceProvider.GetService<ICacheStack>();
        return serviceProvider.GetKeyedService<ICacheStack>(key)
               ?? serviceProvider.GetService<ICacheStack>();
    }
}
