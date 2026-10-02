using System.Linq.Expressions;
using System.Reflection;

namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Immutable registry of entity type → filter expression + pre-resolved MethodInfo.
///     Compiled from the SG-generated <c>FilterMapRegistry</c> (static filters)
///     and merged with dynamic visibility filters at runtime.
/// </summary>
/// <remarks>
///     <para>
///         The FilterMap is the central data structure consumed by
///         <c>PragmaticQueryFilterVisitor</c> to determine which filters
///         to inject into navigation expressions (Include, Select, Any, etc.).
///     </para>
///     <para>
///         Entity types NOT in the map have no filters — the visitor skips them
///         with zero overhead (no dictionary lookup, no TryGetValue).
///     </para>
///     <para>
///         The <c>WhereMethods</c> dictionary contains pre-resolved <c>Enumerable.Where&lt;T&gt;</c>
///         MethodInfo per entity type — generated at compile time by the SG.
///         The visitor uses these directly: zero runtime reflection, fully AOT-compatible.
///     </para>
/// </remarks>
public sealed class FilterMap(
    IReadOnlyDictionary<Type, LambdaExpression> filters,
    IReadOnlyDictionary<Type, MethodInfo> whereMethods)
{
    /// <summary>Shared empty instance — no filters for any type.</summary>
    public static readonly FilterMap Empty = new(
        new Dictionary<Type, LambdaExpression>(),
        new Dictionary<Type, MethodInfo>());

    private readonly IReadOnlyDictionary<Type, LambdaExpression> _filters = filters;
    private readonly IReadOnlyDictionary<Type, MethodInfo> _whereMethods = whereMethods;

    public FilterMap(IReadOnlyDictionary<Type, LambdaExpression> filters)
        : this(filters, new Dictionary<Type, MethodInfo>())
    {
    }

    /// <summary>Number of entity types that have filters in this map.</summary>
    public int Count => _filters.Count;

    /// <summary>Whether this map contains any filters.</summary>
    public bool HasFilters => _filters.Count > 0;

    /// <summary>
    ///     Gets the combined filter expression for the specified entity type.
    /// </summary>
    /// <param name="entityType">The entity type to look up.</param>
    /// <param name="filter">The combined filter expression, or null if no filter exists.</param>
    /// <returns>True if a filter exists for this type.</returns>
    public bool TryGetFilter(Type entityType, out LambdaExpression? filter)
        => _filters.TryGetValue(entityType, out filter);

    /// <summary>
    ///     Gets the pre-resolved <c>Enumerable.Where&lt;T&gt;</c> MethodInfo for the specified entity type.
    ///     SG-generated at compile time — zero runtime reflection.
    /// </summary>
    public bool TryGetWhereMethod(Type entityType, out MethodInfo? method)
        => _whereMethods.TryGetValue(entityType, out method);

    /// <summary>
    ///     Gets the combined filter expression for the specified entity type.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="filter">The typed filter expression, or null if no filter exists.</param>
    /// <returns>True if a filter exists for this type.</returns>
    public bool TryGetFilter<T>(out Expression<Func<T, bool>>? filter) where T : class
    {
        if (_filters.TryGetValue(typeof(T), out var lambda))
        {
            filter = lambda as Expression<Func<T, bool>>;
            // If the stored expression is not of the expected type, treat as no filter
            return filter != null;
        }

        filter = null;
        return false;
    }

    /// <summary>Whether a filter exists for the specified entity type.</summary>
    public bool HasFilterFor(Type entityType) => _filters.ContainsKey(entityType);

    /// <summary>Whether a filter exists for the specified entity type.</summary>
    public bool HasFilterFor<T>() where T : class => _filters.ContainsKey(typeof(T));

    /// <summary>All entity types that have filters in this map.</summary>
    public IEnumerable<Type> FilteredTypes => _filters.Keys;

    /// <summary>
    ///     Creates a new FilterMap by merging this map with additional filters.
    ///     If both maps have a filter for the same type, they are AND-combined.
    /// </summary>
    /// <param name="additional">Additional filters to merge.</param>
    /// <returns>A new immutable FilterMap with the merged filters.</returns>
    public FilterMap Merge(IReadOnlyDictionary<Type, LambdaExpression> additional)
    {
        if (additional.Count == 0)
            return this;
        if (_filters.Count == 0)
            return new FilterMap(additional, _whereMethods);

        var merged = new Dictionary<Type, LambdaExpression>(_filters);

        foreach (var (type, newFilter) in additional)
        {
            if (merged.TryGetValue(type, out var existing))
                merged[type] = CombineWithAnd(existing, newFilter);
            else
                merged[type] = newFilter;
        }

        return new FilterMap(merged, _whereMethods);
    }

    /// <summary>
    ///     Creates a new FilterMap by merging with another FilterMap.
    ///     WhereMethods are combined from both maps.
    /// </summary>
    public FilterMap Merge(FilterMap other)
    {
        if (other._filters.Count == 0)
            return this;
        if (_filters.Count == 0)
        {
            if (_whereMethods.Count == 0)
                return other;
            // Merge where methods: our methods + other's methods (other takes precedence on conflict)
            var mergedWhere = new Dictionary<Type, MethodInfo>(_whereMethods);
            foreach (var (type, method) in other._whereMethods)
                mergedWhere[type] = method;
            return new FilterMap(other._filters, mergedWhere);
        }

        var mergedFilters = new Dictionary<Type, LambdaExpression>(_filters);
        foreach (var (type, newFilter) in other._filters)
        {
            if (mergedFilters.TryGetValue(type, out var existing))
                mergedFilters[type] = CombineWithAnd(existing, newFilter);
            else
                mergedFilters[type] = newFilter;
        }

        // Combine where methods (ours take precedence)
        var mergedMethods = new Dictionary<Type, MethodInfo>(_whereMethods);
        foreach (var (type, method) in other._whereMethods)
            mergedMethods.TryAdd(type, method);

        return new FilterMap(mergedFilters, mergedMethods);
    }

    /// <summary>
    ///     Creates a new FilterMap without the filters of the specified types, preserving the
    ///     pre-resolved WhereMethods of the surviving types. Rebuilding via the filters-only
    ///     constructor would drop them and push the visitor onto its reflection fallback
    ///     (not AOT-safe) for every navigation query.
    /// </summary>
    public FilterMap WithoutTypes(IReadOnlySet<Type> types)
    {
        if (types.Count == 0 || _filters.Count == 0)
            return this;

        var remaining = new Dictionary<Type, LambdaExpression>();
        foreach (var (type, filter) in _filters)
        {
            if (!types.Contains(type))
                remaining[type] = filter;
        }

        if (remaining.Count == _filters.Count)
            return this;
        if (remaining.Count == 0)
            return Empty;

        var remainingMethods = new Dictionary<Type, MethodInfo>();
        foreach (var (type, method) in _whereMethods)
        {
            if (!types.Contains(type))
                remainingMethods[type] = method;
        }

        return new FilterMap(remaining, remainingMethods);
    }

    /// <summary>
    ///     Builds a predicate lambda over a parameter whose type is only known at runtime.
    /// </summary>
    /// <param name="body">The predicate body, written in terms of <paramref name="parameter" />.</param>
    /// <param name="parameter">The entity parameter.</param>
    /// <remarks>
    ///     <para>
    ///         The one place that does this. <c>Expression.Lambda</c> without a type argument has to
    ///         work out <c>Func&lt;T, bool&gt;</c> at runtime, which is a dynamic-code requirement and
    ///         an IL3050 wherever it is written — so it is written once and called from the three
    ///         places that need it, rather than repeated at each.
    ///     </para>
    ///     <para>
    ///         ⚠️ Consolidating the call sites does not remove the requirement, and is not meant to
    ///         read as if it had: composing a filter for a <see cref="Type" /> genuinely needs a
    ///         delegate type built at runtime. What it removes is two copies of the same line.
    ///     </para>
    /// </remarks>
    public static LambdaExpression Predicate(Expression body, ParameterExpression parameter)
        => Expression.Lambda(body, parameter);

    /// <summary>
    ///     Combines two lambda expressions with AND.
    ///     Both expressions must have the same parameter type.
    /// </summary>
    public static LambdaExpression CombineWithAnd(LambdaExpression left, LambdaExpression right)
    {
        // Both should be Expression<Func<T, bool>> for the same T
        var parameter = left.Parameters[0];
        var rightBody = new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body);
        var combined = Expression.AndAlso(left.Body, rightBody);
        return Predicate(combined, parameter);
    }

    /// <summary>
    ///     Replaces one parameter expression with another in an expression tree.
    ///     Required when combining filters from different sources that use different parameter instances.
    /// </summary>
    private sealed class ParameterReplacer(ParameterExpression oldParam, ParameterExpression newParam)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == oldParam ? newParam : base.VisitParameter(node);
    }
}
