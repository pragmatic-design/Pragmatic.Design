using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Actions.Cache;

/// <summary>
///     Where a <c>[Cacheable]</c> action's answer lives for this invocation: the stack its category routes
///     to, the key partitioned for the tenant and the caller, and the declared options.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Partitioned by caller whenever the caller is authenticated, which is stricter than the query
///         executor. That one partitions by user only when a permission-based row filter is registered for
///         the entity, because a query's result is a function of its filters and it knows which apply. An
///         action's body is opaque: it may read the caller, or rows filtered for them, and nothing at
///         compile time says it does not — so sharing an entry between two callers could hand one of them
///         the other's answer. The cost is the hit rate across callers, never a wrong answer.
///     </para>
///     <para>
///         Resolved like the query executor's stack: through <see cref="ICacheStackResolver" />
///         when caching is registered with it, which also answers whether query caching is switched on.
///     </para>
/// </remarks>
/// <param name="Stack">The stack the entry is read from and written to.</param>
/// <param name="Key">The key, partitioned for the tenant and the caller.</param>
/// <param name="Options">The duration and tags <c>[Cacheable]</c> declared.</param>
internal sealed record ActionCacheEntry(ICacheStack Stack, string Key, CacheEntryOptions Options)
{
    /// <summary>
    ///     The entry for <paramref name="cacheable" />, or <c>null</c> when nothing is cached: caching is
    ///     switched off, or no stack is registered.
    /// </summary>
    /// <param name="cacheable">The action, as its generated <c>ICacheable</c>.</param>
    /// <param name="serviceProvider">Where the stack, the tenant and the caller are looked up.</param>
    /// <param name="onNoStackRegistered">
    ///     Called when the action declared a cache and the deployment has none — logged by the caller, not
    ///     thrown: the action still answers, it just answers every time. Not called when caching is
    ///     switched off by configuration, which is a choice and not a misconfiguration.
    /// </param>
    internal static ActionCacheEntry? Open(
        ICacheable cacheable, IServiceProvider serviceProvider, Action onNoStackRegistered)
    {
        var resolver = serviceProvider.GetService<ICacheStackResolver>();

        ICacheStack? stack;
        if (resolver is not null)
        {
            if (!resolver.QueryCachingEnabled)
                return null;
            stack = resolver.ForQuery(cacheable.CacheCategory);
        }
        else
        {
            stack = serviceProvider.GetService<ICacheStack>();
        }

        if (stack is null)
        {
            onNoStackRegistered();
            return null;
        }

        return new ActionCacheEntry(stack, PartitionedKey(cacheable, serviceProvider), cacheable.GetCacheOptions());
    }

    private static string PartitionedKey(ICacheable cacheable, IServiceProvider serviceProvider)
    {
        var tenant = serviceProvider.GetService<ITenantContext>()?.TenantId;
        var caller = serviceProvider.GetService<ICurrentUser>();

        if (tenant is not { Length: > 0 } && caller is not { IsAuthenticated: true })
            return cacheable.GetCacheKey();

        var key = new StringBuilder();
        if (tenant is { Length: > 0 })
            key.Append("t:").Append(tenant).Append(':');

        if (caller is { IsAuthenticated: true })
        {
            key.Append("u:").Append(caller.Id).Append(':');

            // The same subject holds different authority under different delegations — and none under a
            // refused one — so without this one delegation would read the answer computed for another.
            key.Append(DelegatedAuthorityKey.For(caller));
        }

        return key.Append(cacheable.GetCacheKey()).ToString();
    }
}
