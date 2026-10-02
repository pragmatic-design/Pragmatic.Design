using System.Linq.Expressions;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Specification;

namespace Pragmatic.Persistence.Query.Builder;

/// <summary>
///     Fluent builder for constructing type-safe queries with runtime modifications.
///     The query is NOT executed until an explicit execution method is called.
/// </summary>
/// <typeparam name="TEntity">The entity type to query.</typeparam>
/// <remarks>
///     <para>
///         QueryBuilder allows modifying an invariant query definition at runtime.
///         Use for scenarios like:
///         <list type="bullet">
///             <item>Adding extra filters based on runtime conditions</item>
///             <item>Overriding sort order</item>
///             <item>Removing pagination for exports</item>
///             <item>Ignoring certain query filters</item>
///         </list>
///     </para>
///     <para>
///         Eager loading is not among them. This package has no EF dependency, so a navigation cannot be
///         applied to a provider-agnostic <c>IQueryable</c> here — declare it with <c>[EagerLoad]</c> on the
///         query, or let the DTO's <c>RequiredNavigations</c> carry it.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Start from a query definition and modify
/// var builder = query.ToBuilder()
///     .WithFilter(o => o.Priority == Priority.High)
///     .WithSorting(o => o.CreatedAt, descending: true)
///     .IgnoreQueryFilter&lt;SoftDeleteFilter&lt;Order&gt;&gt;()
///
/// var result = await builder.ExecuteAsync(dbContext);
/// </code>
/// </example>
public sealed class QueryBuilder<TEntity> where TEntity : class
{
    private readonly List<Expression<Func<TEntity, bool>>> _filters = [];
    private readonly List<SortExpression> _sorts = [];
    private readonly HashSet<Type> _ignoredFilterTypes = [];
    private int? _skip;
    private int? _take;
    private int _page = 1;
    private int _pageSize = 20;
    private bool _hasPaging;
    private bool _asNoTracking;
    private bool _asSplitQuery;
    private bool _ignoreAllQueryFilters;

    // Hints

    /// <summary>
    ///     Adds a filter condition.
    /// </summary>
    /// <param name="filter">The filter predicate.</param>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> WithFilter(Expression<Func<TEntity, bool>> filter)
    {
        _filters.Add(filter);
        return this;
    }

    /// <summary>
    ///     Adds a filter condition only if the condition is true.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="filter">The filter predicate.</param>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> WithFilter(bool condition, Expression<Func<TEntity, bool>> filter)
    {
        if (condition)
            _filters.Add(filter);
        return this;
    }

    /// <summary>
    ///     Adds a filter from a Specification.
    /// </summary>
    /// <param name="specification">The specification to apply.</param>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> WithFilter(ISpecification<TEntity> specification)
    {
        _filters.Add(specification.ToExpression());
        return this;
    }

    /// <summary>
    ///     Resets all filter conditions.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> ResetFilters()
    {
        _filters.Clear();
        return this;
    }

    /// <summary>
    ///     Sets the primary sort with ascending/descending option.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="keySelector">The key selector.</param>
    /// <param name="descending">Whether to sort descending.</param>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> WithSorting<TKey>(
        Expression<Func<TEntity, TKey>> keySelector,
        bool descending = false)
    {
        _sorts.Clear();
        _sorts.Add(CaptureSort(keySelector,
            descending ? SortDirection.Descending : SortDirection.Ascending, true));
        return this;
    }

    /// <summary>
    ///     Adds ascending sort by the specified key.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="keySelector">The key selector.</param>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        _sorts.Clear();
        _sorts.Add(CaptureSort(keySelector, SortDirection.Ascending, true));
        return this;
    }

    /// <summary>
    ///     Adds descending sort by the specified key.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="keySelector">The key selector.</param>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        _sorts.Clear();
        _sorts.Add(CaptureSort(keySelector, SortDirection.Descending, true));
        return this;
    }

    /// <summary>
    ///     Adds secondary ascending sort.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="keySelector">The key selector.</param>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> ThenBy<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        if (_sorts.Count == 0)
            throw new InvalidOperationException("ThenBy must be called after OrderBy or WithSorting.");

        _sorts.Add(CaptureSort(keySelector, SortDirection.Ascending, false));
        return this;
    }

    /// <summary>
    ///     Adds secondary descending sort.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="keySelector">The key selector.</param>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> ThenByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        if (_sorts.Count == 0)
            throw new InvalidOperationException("ThenByDescending must be called after OrderBy or WithSorting.");

        _sorts.Add(CaptureSort(keySelector, SortDirection.Descending, false));
        return this;
    }

    /// <summary>
    ///     Resets all sorting.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> ResetSorting()
    {
        _sorts.Clear();
        return this;
    }

    /// <summary>
    ///     Sets pagination using page number and size.
    /// </summary>
    /// <param name="page">The page number (1-based).</param>
    /// <param name="pageSize">The page size.</param>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> WithPaging(int page, int pageSize)
    {
        _page = Math.Max(1, page);
        _pageSize = Math.Max(1, pageSize);
        _skip = (_page - 1) * _pageSize;
        _take = _pageSize;
        _hasPaging = true;
        return this;
    }

    /// <summary>
    ///     Removes pagination (returns all results).
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> WithoutPaging()
    {
        _skip = null;
        _take = null;
        _hasPaging = false;
        return this;
    }

    /// <summary>
    ///     Ignores a specific query filter type.
    /// </summary>
    /// <typeparam name="TFilter">The filter type to ignore.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> IgnoreQueryFilter<TFilter>() where TFilter : IQueryFilter
    {
        _ignoredFilterTypes.Add(typeof(TFilter));
        return this;
    }

    /// <summary>
    ///     Ignores all query filters.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> IgnoreQueryFilters()
    {
        _ignoreAllQueryFilters = true;
        return this;
    }

    /// <summary>
    ///     Asks for the result not to be tracked by the change tracker.
    /// </summary>
    /// <remarks>
    ///     Carried out by <see cref="QueryHints.Applier" />, which <c>Pragmatic.Persistence.EFCore</c>
    ///     installs: <c>AsNoTracking</c> is an EF Core extension and this assembly does not reference
    ///     EF Core. ⚠️ With no applier installed, the hint throws rather than being ignored — see
    ///     <see cref="QueryHints" />.
    /// </remarks>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> AsNoTracking()
    {
        _asNoTracking = true;
        return this;
    }

    /// <summary>
    ///     Asks for collection navigations to be loaded with split queries.
    /// </summary>
    /// <remarks>Same channel as <see cref="AsNoTracking" />.</remarks>
    /// <returns>This builder for chaining.</returns>
    public QueryBuilder<TEntity> AsSplitQuery()
    {
        _asSplitQuery = true;
        return this;
    }

    /// <summary>Whether the caller asked for no change tracking.</summary>
    public bool IsNoTracking => _asNoTracking;

    /// <summary>Whether the caller asked for split queries.</summary>
    public bool IsSplitQuery => _asSplitQuery;

    /// <summary>The lifts in scope, plus the ones named on this builder.</summary>
    private static IReadOnlySet<Type> Union(IReadOnlySet<Type> ambient, IReadOnlySet<Type> named)
    {
        if (ambient.Count == 0)
            return named;

        var all = new HashSet<Type>(ambient);
        all.UnionWith(named);
        return all;
    }

    /// <summary>
    ///     Builds the queryable by applying filters, sorting, and paging.
    ///     Does NOT execute the query.
    /// </summary>
    /// <param name="source">The source queryable.</param>
    /// <param name="filterProvider">Optional filter provider for query filters.</param>
    /// <returns>The transformed queryable (not yet executed).</returns>
    public IQueryable<TEntity> Build(IQueryable<TEntity> source, IQueryFilterProvider? filterProvider = null)
    {
        var query = source;

        // The hints first: they change how the provider materialises, not what is selected, and a
        // caller reading the generated SQL expects them where EF puts them.
        if (_asNoTracking || _asSplitQuery)
        {
            var applier = QueryHints.Applier
                ?? throw new InvalidOperationException(
                    $"{nameof(AsNoTracking)}/{nameof(AsSplitQuery)} were asked for, but no "
                    + "IQueryHintApplier is installed. They are EF Core concepts: reference "
                    + "Pragmatic.Persistence.EFCore, which installs one. Ignoring them here is what "
                    + "this builder used to do, and the caller received the opposite of what it asked.");

            query = applier.Apply(query, _asNoTracking, _asSplitQuery);
        }

        // Apply query filters (unless ignored)
        if (!_ignoreAllQueryFilters && filterProvider != null)
        {
            // Honor selective IgnoreQueryFilter<TFilter>() — route through the
            // context-aware overload so the ignored filter types are actually excluded.
            //
            // Starting from the ambient context rather than a fresh one: a FilterContext composed here
            // from nothing carries Mode = Normal whatever the scope asked for, so a read built by hand
            // ignored UseMode entirely. The lifts named on this builder are ADDED to the ones in scope
            // — asking past one filter is not a statement about the others.
            var ambient = filterProvider.AmbientFilterContext();
            var globalFilter = filterProvider.GetCombinedFilter<TEntity>(
                _ignoredFilterTypes.Count > 0
                    ? ambient with { DisabledFilters = Union(ambient.DisabledFilters, _ignoredFilterTypes) }
                    : ambient);
            if (globalFilter != null)
            {
                query = query.Where(globalFilter);
            }
        }

        // Apply builder filters
        foreach (var filter in _filters)
        {
            query = query.Where(filter);
        }

        // Apply sorting
        IOrderedQueryable<TEntity>? orderedQuery = null;
        foreach (var sort in _sorts)
        {
            if (sort.IsPrimary)
                orderedQuery = sort.ApplyToQuery(query);
            else if (orderedQuery != null)
                orderedQuery = sort.ApplyToOrdered(orderedQuery);
        }

        query = orderedQuery ?? query;

        // Apply pagination
        if (_skip.HasValue)
            query = query.Skip(_skip.Value);

        if (_take.HasValue)
            query = query.Take(_take.Value);

        return query;
    }

    /// <summary>
    ///     Gets the combined filter expression (for Count queries).
    /// </summary>
    internal Expression<Func<TEntity, bool>>? GetFilterExpression()
    {
        if (_filters.Count == 0)
            return null;

        var combined = _filters[0];
        for (var i = 1; i < _filters.Count; i++)
        {
            combined = CombineExpressions(combined, _filters[i]);
        }

        return combined;
    }

    /// <summary>
    ///     Gets the current page number.
    /// </summary>
    public int Page => _page;

    /// <summary>
    ///     Gets the current page size.
    /// </summary>
    public int PageSize => _pageSize;

    /// <summary>
    ///     Gets whether paging is enabled.
    /// </summary>
    public bool HasPaging => _hasPaging;



    /// <summary>
    ///     Gets whether all query filters are ignored.
    /// </summary>
    public bool IgnoresAllQueryFilters => _ignoreAllQueryFilters;

    /// <summary>
    ///     Gets the types of query filters to ignore.
    /// </summary>
    public IReadOnlySet<Type> IgnoredFilterTypes => _ignoredFilterTypes;

    private static Expression<Func<TEntity, bool>> CombineExpressions(
        Expression<Func<TEntity, bool>> left,
        Expression<Func<TEntity, bool>> right)
    {
        var parameter = left.Parameters[0];
        var visitor = new ParameterReplacer(right.Parameters[0], parameter);
        var rightBody = visitor.Visit(right.Body);

        return Expression.Lambda<Func<TEntity, bool>>(
            Expression.AndAlso(left.Body, rightBody),
            parameter);
    }

    private sealed class ParameterReplacer(ParameterExpression oldParameter, ParameterExpression newParameter)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == oldParameter ? newParameter : base.VisitParameter(node);
    }

    // Zero reflection: typed delegates captured at call-site when TKey is still known.
    // No GetMethods, MakeGenericMethod, or MethodInfo.Invoke needed.
    private sealed record SortExpression(
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> ApplyToQuery,
        Func<IOrderedQueryable<TEntity>, IOrderedQueryable<TEntity>> ApplyToOrdered,
        SortDirection Direction,
        bool IsPrimary);

    /// <summary>
    ///     Captures typed sort delegates at the call-site where TKey is known,
    ///     avoiding type erasure and the need for reflection in Build().
    /// </summary>
    private static SortExpression CaptureSort<TKey>(
        Expression<Func<TEntity, TKey>> keySelector,
        SortDirection direction,
        bool isPrimary)
    {
        return direction == SortDirection.Ascending
            ? new SortExpression(
                q => q.OrderBy(keySelector),
                q => q.ThenBy(keySelector),
                direction, isPrimary)
            : new SortExpression(
                q => q.OrderByDescending(keySelector),
                q => q.ThenByDescending(keySelector),
                direction, isPrimary);
    }
}
