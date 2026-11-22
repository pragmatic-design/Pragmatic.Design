using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Result;

namespace Pragmatic.Persistence.Caching;

/// <summary>
///     How a <c>[Cacheable]</c> query is read through its cache, on every engine (#882): the stack its category routes to, the key partitioned
///     by whose answer it is, and a failure never cached. EF Core's executor and the engines' generated invokers both call these, so the rule is
///     written once.
/// </summary>
/// <remarks>
///     What differs between the engines is only one input of the key: whether the read depends on the caller. EF Core decides it by rewriting
///     the query's expression (a permission-based filter on the entity, or one a navigation reaches); on the Sql and documents engines the
///     generator decides it from the entity's declared filters, which are known at compile time.
/// </remarks>
public static class QueryCache
{
    /// <summary>
    ///     The stack a cacheable query reads and writes through: the one registered for its <see cref="ICacheable.CacheCategory" />, or
    ///     <paramref name="fallback" /> when no resolver is registered.
    /// </summary>
    /// <remarks>
    ///     The resolver owns both decisions: which stack the category routes to, and whether query caching is on at all. That is where
    ///     <c>CachingOptions.EnableQueryCaching</c> is read, so setting it to false turns caching off for every <c>[Cacheable]</c> query.
    ///     <see cref="ICacheInvalidator" /> is routed the same way by the mutation invoker: the two must agree, or an invalidation addresses a
    ///     namespace the entry was never written to.
    /// </remarks>
    public static ICacheStack? StackFor(ICacheable cacheable, ICacheStackResolver? resolver, ICacheStack? fallback)
    {
        ArgumentNullException.ThrowIfNull(cacheable);
        return resolver is not null ? resolver.ForQuery(cacheable.CacheCategory) : fallback;
    }

    /// <summary>
    ///     The key of <paramref name="rawKey" />, partitioned by every input that shapes the answer: the tenant, the filter mode, the
    ///     disabled filters and, when <paramref name="byCaller" />, the caller and the authority they act with.
    /// </summary>
    /// <remarks>
    ///     Without the partitioning an elevated read could fill an entry that a later read in the normal scope would be served (a leak). With
    ///     none of the inputs set, the key is <paramref name="rawKey" /> as the query declares it.
    /// </remarks>
    /// <param name="rawKey">The key the query declares (<see cref="ICacheable.GetCacheKey" />).</param>
    /// <param name="tenantId">The resolved tenant, or null outside one.</param>
    /// <param name="toggle">The filters the caller lifted, or null when there is no toggle.</param>
    /// <param name="byCaller">True when which rows come back depends on the caller: a permission-based filter the read reaches.</param>
    /// <param name="user">The caller.</param>
    public static string KeyFor(string rawKey, string? tenantId, IQueryFilterToggle? toggle, bool byCaller, ICurrentUser? user)
    {
        ArgumentNullException.ThrowIfNull(rawKey);
        var mode = toggle?.CurrentMode ?? FilterMode.Normal;
        var disabled = toggle?.GetDisabledFilterTypes();

        if (tenantId is null && mode == FilterMode.Normal && (disabled is null || disabled.Count == 0) && !byCaller)
            return rawKey;

        var sb = new StringBuilder();
        if (tenantId is not null)
            sb.Append("t:").Append(tenantId).Append(':');

        sb.Append("m:").Append((int)mode).Append(':');

        if (disabled is not null && disabled.Count > 0)
        {
            sb.Append("d:");
            foreach (var t in disabled.OrderBy(t => t.FullName, StringComparer.Ordinal))
                sb.Append(t.FullName).Append(',');
            sb.Append(':');
        }

        // Two users in the same tenant can legitimately see different rows, so sharing an entry would leak them.
        if (byCaller && user is { IsAuthenticated: true })
        {
            sb.Append("u:").Append(user.Id).Append(':');

            // Under delegation the same subject can hold different authority depending on who is acting, and a permission-based filter reads
            // that authority to decide whether to apply its bypass. Absent a delegation nothing is appended, so existing keys are unchanged.
            sb.Append(DelegatedAuthorityKey.For(user));
        }

        sb.Append(rawKey);
        return sb.ToString();
    }

    /// <summary>A read through <paramref name="stack" /> under <paramref name="key" />: an answer cached, a failure returned and not cached.</summary>
    /// <remarks>A failure is never cached: a transient timeout would otherwise be served for the whole duration.</remarks>
    public static async Task<T> ThroughAsync<T>(
        ICacheStack stack, string key, ICacheable cacheable, Func<CancellationToken, Task<T>> read, Func<T, bool> isFailure, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(cacheable);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(isFailure);
        return await stack.GetOrSetAsync(
            key,
            async token =>
            {
                var result = await read(token).ConfigureAwait(false);
                return isFailure(result) ? CacheFactoryResult<T>.DoNotCache(result) : CacheFactoryResult<T>.Cache(result);
            },
            cacheable.GetCacheOptions(),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     A single-row read through <paramref name="stack" />: the row is what is cached, never the <see cref="Result{T}" /> around it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The cache serializes what it keeps, and a <see cref="Result{T}" /> does not serialize: its <c>Value</c> and <c>Error</c> each
    ///         throw on the side they are not. So the row goes in, and the answer is rebuilt around it. A failure (the row not found) is
    ///         returned as it came and not cached: a row created afterwards is found by the next read.
    ///     </para>
    ///     <para>
    ///         ⚠️ When another caller's read was the one that ran and it found nothing, this caller is handed the "do not cache" placeholder
    ///         rather than that caller's failure; it reads for itself then, because a null it did not produce is not an answer.
    ///     </para>
    /// </remarks>
    public static async Task<Result<T>> ThroughSingleAsync<T>(
        ICacheStack stack, string key, ICacheable cacheable, Func<CancellationToken, Task<Result<T>>> read, CancellationToken ct)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(cacheable);
        ArgumentNullException.ThrowIfNull(read);
        Result<T>? failure = null;
        var row = await stack.GetOrSetAsync<T?>(
            key,
            async token =>
            {
                var result = await read(token).ConfigureAwait(false);
                if (result.IsSuccess)
                    return CacheFactoryResult<T?>.Cache(result.Value);

                failure = result;
                return CacheFactoryResult<T?>.DoNotCache(null);
            },
            cacheable.GetCacheOptions(),
            ct).ConfigureAwait(false);

        if (row is not null)
            return row;

        return failure ?? await read(ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     A <c>[Cacheable]</c> query's read on the Sql or documents engine, through its cache, with every input taken from the request's
    ///     <paramref name="services" />: the stack, the tenant, the filter toggle, the caller. Read straight through when query caching is off or
    ///     no cache is registered.
    /// </summary>
    /// <param name="services">The request's services.</param>
    /// <param name="cacheable">The query.</param>
    /// <param name="byCaller">Whether the entity's declared filters depend on the caller (owner, access scopes, a permission-based rule).</param>
    /// <param name="read">The engine's read.</param>
    /// <param name="isFailure">Whether an answer is a failure, which is never cached.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task<T> ReadAsync<T>(
        IServiceProvider services, ICacheable cacheable, bool byCaller, Func<CancellationToken, Task<T>> read, Func<T, bool> isFailure, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(cacheable);
        ArgumentNullException.ThrowIfNull(read);
        var stack = StackFor(cacheable, services.GetService<ICacheStackResolver>(), services.GetService<ICacheStack>());
        if (stack is null)
            return read(ct);

        var tenant = services.GetService<ITenantContext>()?.TenantId is { Length: > 0 } id ? id : null;
        var key = KeyFor(cacheable.GetCacheKey(), tenant, services.GetService<IQueryFilterToggle>(), byCaller, services.GetService<ICurrentUser>());
        return ThroughAsync(stack, key, cacheable, read, isFailure, ct);
    }
}
