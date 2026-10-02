using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching;

namespace Pragmatic.Authorization.Evaluation;

/// <summary>
///     Resolves the <see cref="ICacheStack"/> that permission entries live in.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="CacheCategories.Permissions"/> exists so that a permission cache can have its
///         own key prefix, its own duration and its own tag namespace. Both sides of this module used
///         the default unkeyed stack instead, which made configuring that category do nothing — and
///         left permission entries sharing a tag namespace with everything else, so a role change
///         evicted every entry tagged <c>user:42</c>, business query caches included. That is the
///         third of the three problems categories are documented to solve.
///     </para>
///     <para>
///         Both the resolver and the invalidator go through here, and they must: a prefixed write
///         invalidated through an unprefixed stack matches nothing and leaves the entry.
///     </para>
/// </remarks>
internal static class PermissionCacheStack
{
    /// <summary>
    ///     The stack registered for <see cref="CacheCategories.Permissions"/>, or the default one when
    ///     the application configured no such category — entries were written there in that case, so
    ///     both sides keep addressing the same namespace.
    /// </summary>
    public static ICacheStack? Resolve(IServiceProvider serviceProvider)
        => typeof(CacheCategories.Permissions).FullName is { } key
            ? serviceProvider.GetKeyedService<ICacheStack>(key) ?? serviceProvider.GetService<ICacheStack>()
            : serviceProvider.GetService<ICacheStack>();
}
