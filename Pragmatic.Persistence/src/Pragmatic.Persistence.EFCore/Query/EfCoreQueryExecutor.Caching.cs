using System.Diagnostics;
using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Caching;
using Pragmatic.Identity;
using Pragmatic.Persistence.EFCore.Diagnostics;
using Pragmatic.Persistence.Query.Executors;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Query.Interfaces;
using Pragmatic.Persistence.Query.Results;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Telemetry;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Persistence.EFCore.Query;

/// <summary>The cache a query reads through, and the key that partitions it by whose answer it is.</summary>
public sealed partial class EfCoreQueryExecutor
{
    /// <summary>
    ///     The stack a cacheable query reads and writes through: the one registered for its
    ///     <see cref="ICacheable.CacheCategory"/>, or the default when it declares none.
    /// </summary>
    /// <remarks>
    ///     Routing has to happen here rather than at construction, because the category belongs to
    ///     the query and this executor serves every query. Before this, a category stack was only
    ///     ever reachable by resolving <see cref="CacheStackProvider"/> by hand: everything declared
    ///     with <c>[Cacheable(Category = ...)]</c> silently used the default namespace, so a category's
    ///     key prefix and duration applied to nothing. <see cref="ICacheInvalidator"/> is routed the
    ///     same way by the mutation invoker — the two must agree or an invalidation addresses a
    ///     namespace the entry was never written to.
    /// </remarks>
    private ICacheStack? ResolveCacheStack(ICacheable? cacheable)
    {
        if (cacheable is null)
            return null;

        // The resolver owns both decisions: which stack the category routes to, and whether query
        // caching is on at all — that is where CachingOptions.EnableQueryCaching is read, so setting it
        // to false turns caching off for every [Cacheable] query.
        if (_cacheStackResolver is not null)
            return _cacheStackResolver.ForQuery(cacheable.CacheCategory);

        return _cacheStack;
    }

    /// <summary>
    ///     A non-paged read through the query's cache when it declares one, exactly as the paged reads are:
    ///     the same key partitioning, the same stack resolution, and a failure never cached.
    /// </summary>
    /// <remarks>
    ///     Every shape asks the query whether it is <see cref="ICacheable" />, not only the paged ones: the
    ///     generator emits the key for every shape and the invoker picks the overload by the shape, so a
    ///     <c>[Cacheable]</c> list or single query read past this would declare its cache and read the
    ///     database every time.
    /// </remarks>
    private async Task<T> ThroughTheCacheAsync<TEntity, T>(
        object query,
        Func<Expression> compose,
        Func<CancellationToken, Task<T>> read,
        Func<T, bool> isFailure,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var cacheable = query as ICacheable;
        var stack = ResolveCacheStack(cacheable);
        LogQueryExecuting(query.GetType().Name, stack is not null);

        if (stack is null || cacheable is null)
            return await read(cancellationToken).ConfigureAwait(false);

        return await stack.GetOrSetAsync(
            BuildCacheKey<TEntity>(cacheable.GetCacheKey(), ReachesAFilteredCollection(compose(), query)),
            async ct2 =>
            {
                var result = await read(ct2).ConfigureAwait(false);
                return isFailure(result)
                    ? CacheFactoryResult<T>.DoNotCache(result)
                    : CacheFactoryResult<T>.Cache(result);
            },
            cacheable.GetCacheOptions(),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     A single-row read through the query's cache: the row is what is cached, never the
    ///     <see cref="Result{T}" /> around it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The cache serializes what it keeps, and a <see cref="Result{T}" /> does not serialize — its
    ///         <c>Value</c> and <c>Error</c> each throw on the side they are not. So the row goes in, and
    ///         the answer is rebuilt around it. A failure (the row not found) is returned as it came and
    ///         not cached: a row created afterwards is found by the next read.
    ///     </para>
    ///     <para>
    ///         ⚠️ When another caller's read was the one that ran and it found nothing, this caller is handed
    ///         the "do not cache" placeholder rather than that caller's failure; it reads for itself then,
    ///         because a null it did not produce is not an answer.
    ///     </para>
    /// </remarks>
    private async Task<Result<T>> ThroughTheCacheSingleAsync<TEntity, T>(
        object query,
        Func<Expression> compose,
        Func<CancellationToken, Task<Result<T>>> read,
        CancellationToken cancellationToken)
        where TEntity : class
        where T : class
    {
        var cacheable = query as ICacheable;
        var stack = ResolveCacheStack(cacheable);
        LogQueryExecuting(query.GetType().Name, stack is not null);

        if (stack is null || cacheable is null)
            return await read(cancellationToken).ConfigureAwait(false);

        Result<T>? failure = null;
        var row = await stack.GetOrSetAsync<T?>(
            BuildCacheKey<TEntity>(cacheable.GetCacheKey(), ReachesAFilteredCollection(compose(), query)),
            async ct2 =>
            {
                var result = await read(ct2).ConfigureAwait(false);
                if (result.IsSuccess)
                    return CacheFactoryResult<T?>.Cache(result.Value);

                failure = result;
                return CacheFactoryResult<T?>.DoNotCache(null);
            },
            cacheable.GetCacheOptions(),
            cancellationToken).ConfigureAwait(false);

        if (row is not null)
            return row;

        return failure ?? await read(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Builds a cache key partitioned by all result-shaping inputs:
    ///     tenant, filter mode, disabled filters, and — when permission-based
    ///     filters are active for <typeparamref name="TEntity"/> — the current user.
    ///     Without this partitioning an elevated query could populate a cache
    ///     slot that a subsequent normal-scope request would then read (data leak).
    /// </summary>
    /// <param name="rawKey">The key the query declares.</param>
    /// <param name="reachesFilteredCollection">
    ///     Whether the composed query reads a collection the navigation filters rewrite — see
    ///     <see cref="ReachesAFilteredCollection" />.
    /// </param>
    internal string BuildCacheKey<TEntity>(string rawKey, bool reachesFilteredCollection = false)
        where TEntity : class
    {
        var mode = _filterToggle?.CurrentMode ?? FilterMode.Normal;
        var disabled = _filterToggle?.GetDisabledFilterTypes();
        var hasPermissionFilters = reachesFilteredCollection || HasPermissionBasedFilters<TEntity>();

        // Read once per key, not once per executor: the tenant can change inside the scope.
        var tenantPrefix = TenantCachePrefix;

        // Fast path: no tenant, no toggle, no permission filters → return raw key
        if (tenantPrefix is null
            && mode == FilterMode.Normal
            && (disabled is null || disabled.Count == 0)
            && !hasPermissionFilters)
        {
            return rawKey;
        }

        var sb = new StringBuilder();
        if (tenantPrefix is not null)
            sb.Append(tenantPrefix);

        sb.Append("m:").Append((int)mode).Append(':');

        if (disabled is not null && disabled.Count > 0)
        {
            sb.Append("d:");
            foreach (var t in disabled.OrderBy(t => t.FullName, StringComparer.Ordinal))
                sb.Append(t.FullName).Append(',');
            sb.Append(':');
        }

        // Partition by user when permission-based filters for this entity are
        // registered. Two users in the same tenant can legitimately see
        // different rows, so sharing cache would leak data.
        if (hasPermissionFilters && _currentUser is { IsAuthenticated: true })
        {
            sb.Append("u:").Append(_currentUser.Id).Append(':');

            // Under delegation the same subject can hold different authority depending on who is
            // acting, and a permission-based filter reads that authority to decide whether to apply
            // its bypass. Partitioning by the subject alone would hand one delegation the rows
            // computed for another — the very leak the user partition above exists to prevent, one
            // level in. Absent a delegation nothing is appended, so existing keys are unchanged.
            sb.Append(DelegatedAuthorityKey.For(_currentUser));
        }

        sb.Append(rawKey);
        return sb.ToString();
    }

    /// <summary>
    ///     Whether the navigation filters rewrite a collection the composed query reads — an
    ///     <c>Include</c>, a predicate such as <c>c.Products.Any(…)</c>, a projection or an aggregate.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Permission-based filters reach collection navigations, so which rows come back can depend on
    ///         the caller even when the root entity has no such filter. The key was partitioned by the
    ///         root alone, and the first caller's answer was served to everyone.
    ///     </para>
    ///     <para>
    ///         ⚠️ Any rewrite counts, not only a permission-based one: the navigation map also merges
    ///         visibility providers, which do not say whether they read the caller. A collection guarded
    ///         only by soft-delete is partitioned too — a cache entry per caller, never a wrong answer.
    ///     </para>
    ///     <para>
    ///         Composing the query costs an expression walk and no database round trip, and it runs on a
    ///         cache hit too, because the key is what finds the hit.
    ///     </para>
    /// </remarks>
    private bool ReachesAFilteredCollection(Expression composed, object query)
    {
        if (query is IQueryHints { IgnoreGlobalFilters: true })
            return false;

        // A declared join reads a set filtered for the caller (FilteredJoinSources), and that filter is
        // already inside the composed query rather than something the visitor rewrites — so it is
        // counted here, on the same terms: a cache entry per caller, never a wrong answer.
        if (query is IJoiningQuery && _filterProvider is not null)
            return true;

        if (_filterMapComposer is null)
            return false;

        var rewritten = _filterMapComposer.ApplyVisitor(composed, GetOrBuildFilterContext());
        return !ReferenceEquals(rewritten, composed);
    }

    /// <summary>The whole read a projected query makes, for the cache key: its projection or aggregate included.</summary>
    private Expression Composed<TEntity, TResult>(IQuery<TEntity, TResult> query, IQueryable<TEntity> source)
        where TEntity : class
        where TResult : class
    {
        var applied = query.Apply(PrepareSource(source, query));
        return Shaped(query, applied)?.Expression ?? applied.Expression;
    }

    private bool HasPermissionBasedFilters<TEntity>() where TEntity : class
    {
        if (_filterProvider is null)
            return false;

        foreach (var filter in _filterProvider.GetFilters<TEntity>())
        {
            if (filter is IPermissionBasedFilter)
                return true;
        }
        return false;
    }
}
