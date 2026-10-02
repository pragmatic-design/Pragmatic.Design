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

/// <summary>
///     EF Core implementation of <see cref="IQueryExecutor"/>.
///     Applies query hints (AsNoTracking, AsSplitQuery), eager loading (Include),
///     global filters, projection, and paging.
/// </summary>
public sealed partial class EfCoreQueryExecutor : IQueryExecutor
{
    private readonly IQueryFilterProvider? _filterProvider;
    private readonly FilterMapComposer? _filterMapComposer;
    private readonly IQueryFilterToggle? _filterToggle;
    private readonly ICacheStack? _cacheStack;
    private readonly ICacheStackResolver? _cacheStackResolver;
    private readonly ICurrentUser? _currentUser;
    private readonly Pragmatic.MultiTenancy.ITenantContext? _tenantContext;

    /// <summary>
    ///     Where a query's declared joins get their target sets.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Optional only because the parameterless constructors exist for tests and for hosts that
    ///     build the executor by hand. A query that declares a key join and finds none says so —
    ///     see <see cref="BindJoinSources" /> — instead of returning rows built from an empty set.
    /// </remarks>
    private readonly IJoinSourceProvider? _joinSources;

    /// <summary>
    ///     The clock this executor asks for the «now» a filter reads.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>FilterContext.Now</c> is public surface — a query filter reads it as
    ///     <c>context.Now</c> — so it comes from the same injected provider as its siblings on this
    ///     path, not from <c>DateTimeOffset.UtcNow</c>. Otherwise a test that fixes the clock and
    ///     expects a temporal filter to see the fixed instant sees the wall clock instead.
    /// </remarks>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     The tenant this query runs under, asked <b>now</b> rather than remembered.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not a field copied in the constructor. Everything downstream reads it: the
    ///     <c>FilterContext</c>, the predicate baked into the filter map
    ///     (<c>tenantId != null &amp;&amp; entity.TenantId == tenantId</c>) and the cache key prefix. A
    ///     snapshot would ignore an operation that deliberately changes tenant inside its scope — which
    ///     is what <c>SetTenant</c> is for — in an executor already built: it would read the old
    ///     tenant's rows and, worse, read and write the old tenant's <b>cache entries</b>.
    ///     <para>
    ///     A snapshot would also make the tenant store's shape matter. A per-scope field is written
    ///     before anything in the scope is constructed, so copying at construction happens to be right;
    ///     an ambient value has to be on the flow that constructs, and where it is not the copy is null
    ///     — which turns the baked predicate into a constant false and a filtered search into zero rows.
    ///     Asking at use removes the dependence on when the value arrives.
    ///     </para>
    /// </remarks>
    private string? TenantId => _tenantContext?.TenantId is { Length: > 0 } tid ? tid : null;

    /// <summary>The cache-key prefix for the current tenant, or null when none is resolved.</summary>
    private string? TenantCachePrefix => TenantId is { } tid ? $"t:{tid}:" : null;
    private readonly ILogger<EfCoreQueryExecutor> _logger;

    /// <summary>
    ///     Creates a new executor without global filter or cache support.
    /// </summary>
    public EfCoreQueryExecutor()
    {
        _logger = NullLogger<EfCoreQueryExecutor>.Instance;
        _timeProvider = TimeProvider.System;
    }

    /// <summary>
    ///     Creates a new executor with global filter support (soft-delete, tenant, etc.).
    /// </summary>
    /// <param name="filterProvider">The provider for global query filters.</param>
    public EfCoreQueryExecutor(IQueryFilterProvider filterProvider)
    {
        _filterProvider = filterProvider;
        _logger = NullLogger<EfCoreQueryExecutor>.Instance;
        _timeProvider = TimeProvider.System;
    }

    /// <summary>
    ///     Creates a new executor with global filter and cache support.
    /// </summary>
    /// <param name="filterProvider">The provider for global query filters.</param>
    /// <param name="cacheStack">
    ///     The cache stack for transparent query result caching.
    ///     Queries implementing <see cref="ICacheable"/> will be cached automatically.
    ///     Pass <c>null</c> to disable caching.
    /// </param>
    /// <param name="logger">Optional logger. When null, logging is suppressed.</param>
    public EfCoreQueryExecutor(IQueryFilterProvider? filterProvider, ICacheStack? cacheStack,
        ILogger<EfCoreQueryExecutor>? logger = null)
    {
        _filterProvider = filterProvider;
        _cacheStack = cacheStack;
        _logger = logger ?? NullLogger<EfCoreQueryExecutor>.Instance;
        _timeProvider = TimeProvider.System;
    }

    /// <summary>
    ///     Creates a new executor with full filter pipeline support including navigation filters.
    /// </summary>
    public EfCoreQueryExecutor(
        IQueryFilterProvider? filterProvider,
        FilterMapComposer? filterMapComposer,
        IQueryFilterToggle? filterToggle,
        ICacheStack? cacheStack,
        ILogger<EfCoreQueryExecutor>? logger = null,
        Pragmatic.MultiTenancy.ITenantContext? tenantContext = null,
        ICurrentUser? currentUser = null,
        ICacheStackResolver? cacheStackResolver = null,
        TimeProvider? timeProvider = null,
        IJoinSourceProvider? joinSources = null)
    {
        _filterProvider = filterProvider;
        _filterMapComposer = filterMapComposer;
        _filterToggle = filterToggle;
        _cacheStack = cacheStack;
        _cacheStackResolver = cacheStackResolver;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _logger = logger ?? NullLogger<EfCoreQueryExecutor>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _joinSources = joinSources;
    }

    /// <inheritdoc />
    public async Task<PagedResult<TEntity>> ExecuteAsync<TEntity>(
        IPagedQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        var cacheable = query as ICacheable;
        var stack = ResolveCacheStack(cacheable);
        LogQueryExecuting(query.GetType().Name, stack is not null);

        if (stack is not null && cacheable is not null)
        {
            return await stack.GetOrSetAsync(
                BuildCacheKey<TEntity>(cacheable.GetCacheKey(),
                    ReachesAFilteredCollection(query.Apply(PrepareSource(source, query)).Expression, query)),
                // Never cache a failed query — a transient timeout/connection blip would
                // otherwise be served for the whole TTL, turning a one-off into a prolonged outage.
                async ct2 =>
                {
                    var result = await ExecutePagedInternalAsync(query, source, ct2).ConfigureAwait(false);
                    return result.IsFailure
                        ? CacheFactoryResult<PagedResult<TEntity>>.DoNotCache(result)
                        : CacheFactoryResult<PagedResult<TEntity>>.Cache(result);
                },
                cacheable.GetCacheOptions(),
                cancellationToken).ConfigureAwait(false);
        }

        return await ExecutePagedInternalAsync(query, source, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PagedResult<TResult>> ExecuteAsync<TEntity, TResult>(
        IPagedQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TResult : class
    {
        var cacheable = query as ICacheable;
        var stack = ResolveCacheStack(cacheable);
        LogQueryExecuting(query.GetType().Name, stack is not null);

        if (stack is not null && cacheable is not null)
        {
            return await stack.GetOrSetAsync(
                BuildCacheKey<TEntity>(cacheable.GetCacheKey(),
                    ReachesAFilteredCollection(Composed(query, source), query)),
                // Never cache a failed query (see the non-projected overload above).
                async ct2 =>
                {
                    var result = await ExecutePagedProjectedInternalAsync(query, source, ct2).ConfigureAwait(false);
                    return result.IsFailure
                        ? CacheFactoryResult<PagedResult<TResult>>.DoNotCache(result)
                        : CacheFactoryResult<PagedResult<TResult>>.Cache(result);
                },
                cacheable.GetCacheOptions(),
                cancellationToken).ConfigureAwait(false);
        }

        return await ExecutePagedProjectedInternalAsync(query, source, cancellationToken).ConfigureAwait(false);
    }


    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> ExecuteAllAsync<TEntity>(
        IQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default) where TEntity : class
        => ThroughTheCacheAsync<TEntity, IReadOnlyList<TEntity>>(
            query, () => query.Apply(PrepareSource(source, query)).Expression,
            ct => ExecuteAllInternalAsync(query, source, ct), static _ => false, cancellationToken);

    private async Task<IReadOnlyList<TEntity>> ExecuteAllInternalAsync<TEntity>(
        IQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken) where TEntity : class
    {
        var queryable = PrepareSource(source, query);
        var filtered = query.Apply(queryable);
        filtered = ApplyGlobalFilters(filtered, query);

        return await filtered.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TResult>> ExecuteAllAsync<TEntity, TResult>(
        IQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TResult : class
        => ThroughTheCacheAsync<TEntity, IReadOnlyList<TResult>>(
            query, () => Composed(query, source),
            ct => ExecuteAllProjectedInternalAsync(query, source, ct), static _ => false, cancellationToken);

    private async Task<IReadOnlyList<TResult>> ExecuteAllProjectedInternalAsync<TEntity, TResult>(
        IQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken)
        where TEntity : class
        where TResult : class
    {
        var queryable = PrepareSource(source, query);
        var filtered = ApplyGlobalFilters(query.Apply(queryable), query, navigation: false);

        // The whole step, not a row projection: the rows of a grouping are not rows of the entity.
        if (Shaped(query, filtered) is { } shaped)
            return await ApplyNavigationFilters(shaped, query).ToListAsync(cancellationToken).ConfigureAwait(false);

        var rows = ApplyNavigationFilters(filtered, query);

        // ⚠️ The rows first, the mapping after. Everything that narrows the set has already been
        // applied to `rows`, so this materialises what was coming back anyway — not a table.
        if (query.MapEach is { } map)
        {
            var materialised = await rows.ToListAsync(cancellationToken).ConfigureAwait(false);
            return materialised.Select(map).ToList();
        }

        return await rows.OfType<TResult>().ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<TEntity>> ExecuteSingleAsync<TEntity>(
        IQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default) where TEntity : class
        => ThroughTheCacheSingleAsync<TEntity, TEntity>(
            query, () => query.Apply(PrepareSource(source, query)).Expression,
            ct => ExecuteSingleInternalAsync(query, source, ct), cancellationToken);

    private async Task<Result<TEntity>> ExecuteSingleInternalAsync<TEntity>(
        IQuery<TEntity> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken) where TEntity : class
    {
        var queryable = PrepareSource(source, query);
        var filtered = ApplyGlobalFilters(query.Apply(queryable), query);

        // FirstOrDefault, not Single: see IQueryExecutor.ExecuteSingleAsync.
        var entity = await filtered.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return entity is null
            ? Result<TEntity>.Failure(NotFoundError.For(typeof(TEntity).Name))
            : entity;
    }

    /// <inheritdoc />
    public Task<Result<TResult>> ExecuteSingleAsync<TEntity, TResult>(
        IQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TResult : class
        => ThroughTheCacheSingleAsync<TEntity, TResult>(
            query, () => Composed(query, source),
            ct => ExecuteSingleProjectedInternalAsync(query, source, ct), cancellationToken);

    private async Task<Result<TResult>> ExecuteSingleProjectedInternalAsync<TEntity, TResult>(
        IQuery<TEntity, TResult> query,
        IQueryable<TEntity> source,
        CancellationToken cancellationToken)
        where TEntity : class
        where TResult : class
    {
        var queryable = PrepareSource(source, query);
        var filtered = ApplyGlobalFilters(query.Apply(queryable), query, navigation: false);

        if (query.Aggregate is not null)
        {
            var group = await ApplyNavigationFilters(Shaped(query, filtered)!, query)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return group is null
                ? Result<TResult>.Failure(NotFoundError.For(typeof(TEntity).Name))
                : group;
        }

        if (query.Projection is null && query.MapEach is { } mapOne)
        {
            var row = await ApplyNavigationFilters(filtered, query).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            // The same NotFound the projected path answers with, and for the same reason: the entity
            // is what the caller asked about, not the shape it wanted back.
            return row is null
                ? Result<TResult>.Failure(NotFoundError.For(typeof(TEntity).Name))
                : mapOne(row);
        }

        var result = query.Projection is not null
            ? await ApplyNavigationFilters(filtered.Select(query.Projection), query).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : await ApplyNavigationFilters(filtered, query).OfType<TResult>().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        // The entity name, not the projection's: "Book not found" is what the caller asked about.
        return result is null
            ? Result<TResult>.Failure(NotFoundError.For(typeof(TEntity).Name))
            : result;
    }

    private static QueryError MapToQueryError(Exception ex)
    {
        return ex switch
        {
            TimeoutException timeout => new QueryError.Timeout
            {
                Message = timeout.Message,
                TimeoutDuration = null
            },
            InvalidOperationException ioe when ioe.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)
                => new QueryError.Connection { Message = ioe.Message },
            _ => new QueryError.Database { Message = ex.Message, Inner = ex }
        };
    }
}
