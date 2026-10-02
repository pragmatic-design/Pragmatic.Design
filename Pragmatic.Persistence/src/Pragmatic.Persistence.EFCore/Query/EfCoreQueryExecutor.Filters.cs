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

/// <summary>The source a query starts from, and the filters every part of it is read through.</summary>
public sealed partial class EfCoreQueryExecutor
{
    /// <summary>
    ///     Applies query hints (AsNoTracking, AsSplitQuery, IgnoreQueryFilters)
    ///     and eager loading (Include) to the source queryable.
    /// </summary>
    private IQueryable<TEntity> PrepareSource<TEntity>(
        IQueryable<TEntity> source,
        object query) where TEntity : class
    {
        BindJoinSources(query);

        var queryable = source;

        // Apply hints — default to AsNoTracking when no hints specified
        if (query is IQueryHints hints)
        {
            if (hints.NoTracking)
                queryable = queryable.AsNoTracking();
            if (hints.SplitQuery)
                queryable = queryable.AsSplitQuery();
            if (hints.IgnoreGlobalFilters)
                queryable = queryable.IgnoreQueryFilters();
        }
        else
        {
            queryable = queryable.AsNoTracking();
        }

        // Apply eager loading from declared include paths
        if (query is IIncludableQuery<TEntity> includable)
        {
            foreach (var path in includable.IncludePaths)
                queryable = queryable.Include(path);
        }

        return queryable;
    }

    /// <summary>
    ///     Hands a joining query the sets its declared joins read, before anything reads its
    ///     <c>Aggregate</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The refusal is deliberate and immediate. Leaving the sources unbound would surface later,
    ///     from inside a generated lambda, as a null reference with no name on it — and binding an
    ///     empty set instead would answer with rows silently missing every joined column.
    /// </remarks>
    private void BindJoinSources(object query)
    {
        if (query is not IJoiningQuery joining)
            return;

        if (_joinSources is null)
        {
            throw new InvalidOperationException(
                $"'{query.GetType().Name}' declares a [Join<T>(ForeignKey = …)], which reads an entity "
                + "no navigation reaches, but this executor was built without an IJoinSourceProvider. "
                + "Register the executor through AddAllPragmaticDbContexts, or pass one to its constructor.");
        }

        // Filtered like the rest of the query, unless the query asked for none: IgnoreGlobalFilters
        // lifts the whole pipeline, and a join is part of it.
        var filtered = _filterProvider is null || query is IQueryHints { IgnoreGlobalFilters: true }
            ? _joinSources
            : new FilteredJoinSources(_joinSources, _filterProvider, GetOrBuildFilterContext());

        joining.BindJoinSources(filtered);
    }

    /// <param name="queryable">The composed query.</param>
    /// <param name="query">The query object, for its hints.</param>
    /// <param name="navigation">
    ///     Whether to filter collection navigations here too. <see langword="false" /> when a projection or
    ///     an aggregate is still to come: those read collections the visitor must see, and it visits the
    ///     final query once — see <see cref="ApplyNavigationFilters{T}" />.
    /// </param>
    private IQueryable<TEntity> ApplyGlobalFilters<TEntity>(IQueryable<TEntity> queryable, object query, bool navigation = true)
        where TEntity : class
    {
        if (_filterProvider is null && _filterMapComposer is null)
            return queryable;

        // IgnoreGlobalFilters must bypass the FULL Pragmatic filter pipeline (root + nav),
        // not only EF Core's model-level HasQueryFilter (handled in PrepareSource). Admin/raw/support
        // views set this hint and rightly expect soft-deleted / tenant-hidden / permission-filtered
        // rows; re-applying Pragmatic filters here would silently keep them hidden.
        if (query is IQueryHints { IgnoreGlobalFilters: true })
            return queryable;

        // Build a shared FilterContext for consistent behavior across root and navigation filters.
        // Populate TenantId/UserId so navigation (Include/projection) filters apply the
        // same tenant and ownership/authorization scope as root filters — otherwise included child
        // collections could surface rows from another tenant or outside the caller's scope.
        var context = GetOrBuildFilterContext();

        // The tenant rule is enforced twice: by the Pragmatic ITenantFilter below, and by the EF Core
        // named query filter the generated DbContext installs. FilterMode governed only the first, so
        // FilterMode.Background — whose whole purpose is "background jobs that operate across
        // tenants" — lifted one of two filters and changed nothing: a job asking for it still read
        // zero rows. Found by writing the first background job in a consumer application.
        //
        // Named filters are what make this surgical. Background keeps SoftDelete, as its summary
        // promises, and drops only Tenant; Raw drops everything, which is what "no automatic filters"
        // has to mean when half of them live in EF's model.
        //
        // The names are gathered into ONE call on purpose: EF Core keeps the last IgnoreQueryFilters
        // in the chain, so a second call would replace the first set rather than add to it.
        if (context.IsRaw)
        {
            queryable = queryable.IgnoreQueryFilters();
        }
        else
        {
            var lifted = CollectLiftedFilterNames(context);
            if (lifted is not null)
                queryable = queryable.IgnoreQueryFilters(lifted);
        }

        // Root-level filters (IQueryFilterProvider — soft-delete, tenant, authorization)
        // Uses FilterContext-aware overload to respect FilterMode and DisabledFilters
        if (_filterProvider is not null)
        {
            // Observability: trace which filters are active for this entity type
            var activeFilters = _filterProvider.GetFilters<TEntity>().ToList();
            if (activeFilters.Count > 0 && _logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                var filterNames = string.Join(", ", activeFilters.Select(f => f.GetType().Name));
                _logger.LogDebug("Applying {FilterCount} filter(s) to {EntityType}: {FilterNames}",
                    activeFilters.Count, typeof(TEntity).Name, filterNames);
            }

            var activity = System.Diagnostics.Activity.Current;
            if (activity is not null && activeFilters.Count > 0)
            {
                activity.SetTag(DbTags.FilterCount, activeFilters.Count);
                activity.SetTag(DbTags.FilterMode, context.Mode.ToString());
                activity.SetTag(DbTags.FilterNames,
                    string.Join(",", activeFilters.Select(f => f.GetType().Name)));
            }

            var combinedFilter = _filterProvider.GetCombinedFilter<TEntity>(context);
            if (combinedFilter is not null)
                queryable = queryable.Where(combinedFilter);
        }

        // Navigation-level filters (FilterMapComposer — filters on Include/ThenInclude navigations)
        if (navigation && _filterMapComposer is not null)
        {
            queryable = _filterMapComposer.ApplyNavigationFilters(queryable, context);
        }

        return queryable;
    }

    /// <summary>
    ///     Filters the collection navigations the final query reads — once, over the whole of it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A projection and an aggregate are applied after the root filters, so the visitor runs after
    ///     them too: run before, a collection read inside one — <c>c.Products.Count(…)</c> in a
    ///     <c>Select</c> — would not be filtered at all, and the rows a permission filter withholds would
    ///     be counted. Visiting the final query
    ///     also covers what <c>Apply</c> read, so it runs once and never twice over the same navigation.
    /// </remarks>
    private IQueryable<T> ApplyNavigationFilters<T>(IQueryable<T> queryable, object query)
        where T : class
    {
        if (_filterMapComposer is null || query is IQueryHints { IgnoreGlobalFilters: true })
            return queryable;

        return _filterMapComposer.ApplyNavigationFilters(queryable, GetOrBuildFilterContext());
    }

    /// <summary>The aggregate or the projection over <paramref name="filtered" />, or null for neither.</summary>
    private static IQueryable<TResult>? Shaped<TEntity, TResult>(IQuery<TEntity, TResult> query, IQueryable<TEntity> filtered)
        where TEntity : class
        where TResult : class
        => query.Aggregate is { } aggregate ? aggregate(filtered)
            : query.Projection is { } projection ? filtered.Select(projection)
            : null;

    // Reuse one FilterContext instance per (Mode, DisabledFilters, tenant, user) key.
    // A fresh `new FilterContext` per query defeats FilterMapComposer's per-scope static-map cache,
    // which keys on ReferenceEquals(context). The toggle state can change within a scope, so we
    // re-build only when the value key actually changes — otherwise we hand back the same instance.
    /// <summary>
    ///     The name the generated <c>DbContext</c> gives its tenant query filter.
    /// </summary>
    /// <remarks>
    ///     A literal shared by two generators and this executor. It is written here rather than
    ///     derived because the alternative — matching on the filter expression — would be a third
    ///     derivation of the same fact; if the name ever changes, this is the one place that has to
    ///     change with it, and the tests below fail loudly if it does not.
    /// </remarks>
    private const string TenantQueryFilterName = "Tenant";

    /// <summary>
    ///     The EF Core named query filters to lift for this query, or <c>null</c> when there are none.
    /// </summary>
    /// <remarks>
    ///     Two sources meet here. <c>FilterMode.Background</c> lifts the tenant filter, which is what
    ///     "operates across tenants" has to mean when half of that rule lives in the EF model. And a
    ///     <c>[WithoutFilter&lt;TRule&gt;]</c> on an operation lifts that rule by the name the
    ///     generator gave it — the same mechanism, reached from a declaration instead of a mode.
    /// </remarks>
    private static IReadOnlyList<string>? CollectLiftedFilterNames(FilterContext context)
    {
        var names = context.DisabledQueryFilterNames;
        if (names.Count == 0)
            return context.SkipTenant ? [TenantQueryFilterName] : null;

        var lifted = new List<string>(names.Count + 1);
        lifted.AddRange(names);
        if (context.SkipTenant)
            lifted.Add(TenantQueryFilterName);
        return lifted;
    }

    private FilterContext? _cachedFilterContext;
    private (FilterMode Mode, int DisabledHash, string? TenantId, string? UserId) _cachedFilterKey;

    /// <summary>
    ///     The context every filter of this scope is built from, cached by what it depends on.
    /// </summary>
    /// <remarks>
    ///     Internal so a test can read the «now» it carries: the value is otherwise reachable only
    ///     through a filter that happens to use it, which tests the filter rather than the clock.
    /// </remarks>
    internal FilterContext GetOrBuildFilterContext()
    {
        var mode = _filterToggle?.CurrentMode ?? FilterMode.Normal;
        var disabled = _filterToggle?.GetDisabledFilterTypes() ?? new HashSet<Type>();
        var liftedNames = _filterToggle?.GetDisabledQueryFilterNames() ?? new HashSet<string>();
        var userId = _currentUser is { IsAuthenticated: true } ? _currentUser.Id : null;
        var key = (mode, ComputeDisabledHash(disabled) ^ ComputeLiftedHash(liftedNames), TenantId, userId);

        if (_cachedFilterContext is not null && _cachedFilterKey == key)
            return _cachedFilterContext;

        var context = new FilterContext
        {
            Mode = mode,
            DisabledFilters = disabled,
            DisabledQueryFilterNames = liftedNames,
            Now = _timeProvider.GetUtcNow(),
            TenantId = TenantId,
            UserId = userId
        };

        _cachedFilterContext = context;
        _cachedFilterKey = key;
        return context;
    }

    // Order-independent hash of the disabled-filter type set so the key is stable regardless of
    // enumeration order (HashSet has no defined order).
    private static int ComputeDisabledHash(IReadOnlySet<Type> disabled)
    {
        if (disabled.Count == 0)
            return 0;

        var hash = 0;
        foreach (var t in disabled)
            hash ^= t.GetHashCode();
        return hash;
    }

    // Same order-independent shape for the lifted EF filter names. Ordinal, because these are
    // generated identifiers and two that differ only by case are two different filters.
    private static int ComputeLiftedHash(IReadOnlySet<string> lifted)
    {
        if (lifted.Count == 0)
            return 0;

        var hash = 0;
        foreach (var name in lifted)
            hash ^= StringComparer.Ordinal.GetHashCode(name);
        return hash;
    }
}
