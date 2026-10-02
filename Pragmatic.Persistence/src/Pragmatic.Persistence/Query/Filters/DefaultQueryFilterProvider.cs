using System.Collections.Concurrent;
using System.Linq.Expressions;
using Pragmatic.Identity;

namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Default implementation of <see cref="IQueryFilterProvider"/>.
///     Aggregates all <see cref="IQueryFilter"/> instances from DI,
///     respects <see cref="IQueryFilterToggle"/> for scoped disabling,
///     and handles <see cref="IPermissionBasedFilter{T}"/> permission checks.
/// </summary>
public sealed partial class DefaultQueryFilterProvider(
    IEnumerable<IQueryFilter> filters,
    IQueryFilterTypeRegistry registry,
    IQueryFilterToggle? toggle = null,
    ICurrentUser? currentUser = null,
    QueryFilterOptions? options = null,
    TimeProvider? clock = null)
    : IQueryFilterProvider
{
    // The clock this provider stamps an ambient context with. Optional so nothing that constructs
    // this by hand has to change; named here rather than read at the point of use, because a
    // DateTimeOffset.UtcNow inside AmbientFilterContext is exactly the defect FilterContext.Now was
    // required to close.
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private readonly QueryFilterOptions _options = options ?? new QueryFilterOptions();

    private readonly IReadOnlyList<IQueryFilter> _allFilters = filters.ToList();

    // Cache: entity type → list of applicable IQueryFilter instances (untyped)
    // This cache is stable — toggle/permission checks happen at query time, not at cache time
    private readonly ConcurrentDictionary<Type, IReadOnlyList<IQueryFilter>> _filtersByEntityType = new();

    public IEnumerable<IQueryFilter<T>> GetFilters<T>() where T : class
    {
        var filters = GetFiltersForType(typeof(T));

        foreach (var filter in filters)
        {
            if (filter is not IQueryFilter<T> typed)
                continue;
            if (IsFilterDisabled(filter))
                continue;
            if (IsPermissionBypassed(filter))
                continue;

            yield return typed;
        }
    }

    public Expression<Func<T, bool>>? GetCombinedFilter<T>(NavigationContext? context = null) where T : class
        => GetCombinedFilter<T>(AmbientFilterContext(), context);

    /// <inheritdoc />
    public FilterContext AmbientFilterContext()
        => FilterContext.At(_clock) with
        {
            Mode = toggle?.CurrentMode ?? FilterMode.Normal,
            DisabledFilters = toggle?.GetDisabledFilterTypes() ?? EmptyTypes,
            DisabledQueryFilterNames = toggle?.GetDisabledQueryFilterNames() ?? EmptyNames,
            UserId = currentUser is { IsAuthenticated: true } ? currentUser.Id : null,
            TenantId = currentUser?.TenantId
        };

    private static readonly IReadOnlySet<Type> EmptyTypes = new HashSet<Type>();
    private static readonly IReadOnlySet<string> EmptyNames = new HashSet<string>();

    public Expression<Func<T, bool>>? GetCombinedFilter<T>(FilterContext filterContext, NavigationContext? navigationContext = null) where T : class
        => (Expression<Func<T, bool>>?)GetCombinedFilter(typeof(T), filterContext, navigationContext);

    /// <inheritdoc />
    /// <remarks>
    ///     <para>
    ///         The one place the composition happens. The generic overloads above cast the result: the
    ///         lambda is built over a parameter of exactly <c>entityType</c>, so an
    ///         <c>Expression&lt;Func&lt;T, bool&gt;&gt;</c> is what it already is.
    ///     </para>
    ///     <para>
    ///         Untyped rather than the other way round because the navigation map is keyed by
    ///         <see cref="Type" /> and has no generic parameter to offer. Before this, the map was built
    ///         by a separate adapter that ANDed whatever it found and skipped every
    ///         <c>IPermissionBasedFilter</c> outright — because ANDing an additive scope group is
    ///         strictly narrower than the root, and hiding rows the root shows is the worse of the two
    ///         mistakes. Both sides now read this.
    ///     </para>
    /// </remarks>
    public LambdaExpression? GetCombinedFilter(
        Type entityType, FilterContext filterContext, NavigationContext? navigationContext = null)
    {
        Ensure.Ensure.ThrowIfNull(entityType);

        if (filterContext.IsRaw)
            return null;

        // Fail closed for an anonymous caller when a permission-based filter applies that neither the
        // mode nor the caller lifted (trusted modes legitimately bypass).
        if (ShouldFailClosed(entityType, filterContext))
            return AlwaysFalse(entityType);

        return CombineFilters(entityType, GetFiltersForContext(entityType, filterContext), navigationContext);
    }

    /// <summary>The predicate that lets nothing through, for the entity type at hand.</summary>
    private static LambdaExpression AlwaysFalse(Type entityType)
        => FilterMap.Predicate(Expression.Constant(false), Expression.Parameter(entityType, "_"));

    /// <summary>
    ///     Whether a filter is the one for this entity type.
    /// </summary>
    /// <remarks>
    ///     Stands in for the <c>is IQueryFilter&lt;T&gt;</c> guard the typed path used. Equivalent:
    ///     generic interfaces are invariant, so that test matched exactly one entity type, which is
    ///     what <see cref="IQueryFilter.EntityType" /> reports.
    /// </remarks>
    private static bool IsFor(IQueryFilter filter, Type entityType)
        => filter.EntityType == entityType;

    /// <summary>
    ///     Gets filters for a specific entity type, respecting <see cref="FilterContext"/>
    ///     for mode-based skipping and selective disabling.
    /// </summary>
    private IEnumerable<IQueryFilter> GetFiltersForContext(Type entityType, FilterContext filterContext)
    {
        var applicable = new List<IQueryFilter>();
        var aScopeMemberWasLifted = false;

        foreach (var filter in GetFiltersForType(entityType))
        {
            if (!IsFor(filter, entityType))
                continue;

            if (IsLifted(filter, entityType, filterContext))
            {
                // An additive group is lifted whole or not at all — see LiftsTheWholeScopeGroup.
                if (filter is IScopeVisibilityFilter)
                    aScopeMemberWasLifted = true;

                continue;
            }

            applicable.Add(filter);
        }

        // ⚠️ Removing one disjunct from an OR is a narrowing, not a lift, so the scope members go
        // together. Decided each on its own, a scope-visibility filter that is not
        // IPermissionBasedFilter would survive FilterMode.Admin while its sibling was dropped, the OR
        // group would collapse to that single predicate, and the administrative read would come back
        // with strictly fewer rows than an ordinary one. A typed lift of one member would do the
        // same, and report a legitimate row as a conflict.
        return aScopeMemberWasLifted
            ? applicable.Where(static f => f is not IScopeVisibilityFilter)
            : applicable;
    }

    private static LambdaExpression? CombineFilters(
        Type entityType, IEnumerable<IQueryFilter> filters, NavigationContext? context)
    {
        // Scope-visibility filters (ownership / materialized scopes / computed rules)
        // are ADDITIVE — a row is visible if it matches ANY of them — so they must be OR-composed
        // together, then AND-composed with the remaining restrictive filters (soft-delete, tenant).
        var parameter = Expression.Parameter(entityType, "e");

        Expression? restrictive = null;   // AND-composed
        Expression? scopeVisibility = null; // OR-composed group

        foreach (var filter in filters)
        {
            if (context is not null && !ShouldApplyInContext(filter, context))
                continue;

            if (filter.GetFilterExpression() is not { } filterExpr)
                continue;

            var body = new ParameterReplacer(filterExpr.Parameters[0], parameter).Visit(filterExpr.Body);

            if (filter is IScopeVisibilityFilter)
            {
                // A pass-through (_ => true) contributes nothing to the OR group; skipping it prevents
                // an empty/no-rule scope filter from collapsing visibility to "all rows".
                if (IsUnconditionalTrue(body))
                    continue;

                scopeVisibility = scopeVisibility is null
                    ? body
                    : Expression.OrElse(scopeVisibility, body);
            }
            else
            {
                restrictive = restrictive is null
                    ? body
                    : Expression.AndAlso(restrictive, body);
            }
        }

        var combinedBody = (restrictive, scopeVisibility) switch
        {
            (null, null) => (Expression?)null,
            (not null, null) => restrictive,
            (null, not null) => scopeVisibility,
            _ => Expression.AndAlso(restrictive!, scopeVisibility!)
        };

        return combinedBody is null
            ? null
            : FilterMap.Predicate(combinedBody, parameter);
    }

    /// <summary>True when the expression is the constant <c>true</c> (an unconditional pass-through).</summary>
    private static bool IsUnconditionalTrue(Expression body)
        => body is ConstantExpression { Value: true };

    public bool HasFilters<T>() where T : class
    {
        var filters = GetFiltersForType(typeof(T));

        foreach (var filter in filters)
        {
            if (filter is not IQueryFilter<T>)
                continue;
            if (IsFilterDisabled(filter))
                continue;
            if (IsPermissionBypassed(filter))
                continue;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Gets all registered filters that target a specific entity type.
    ///     Cached per entity type (stable — toggle checks happen at query time).
    /// </summary>
    private IReadOnlyList<IQueryFilter> GetFiltersForType(Type entityType)
    {
        return _filtersByEntityType.GetOrAdd(entityType, _ =>
            _allFilters
                .Where(f => registry.ImplementsFilterFor(f.GetType(), entityType))
                .OrderBy(f => f.Priority)
                .ToList());
    }

    private static bool ShouldApplyInContext(IQueryFilter filter, NavigationContext context)
    {
        // A collection and a joined set answer by where the query reads them — the position the
        // context carries. References are not visited, so theirs is only the kind of navigation.
        var scope = filter.Scope;
        var matchesScope = context.Depth == 0
            ? scope.HasFlag(FilterScope.Root)
            : context.IsCollection || context.Position == FilterScope.Joins
                ? scope.HasFlag(context.Position)
                : context.IsRequired
                    ? scope.HasFlag(FilterScope.RequiredReferences)
                    : scope.HasFlag(FilterScope.OptionalReferences);

        if (!matchesScope)
            return false;

        // Fine-grained control via ShouldApplyTo
        return filter.ShouldApplyTo(context);
    }

    /// <summary>
    ///     Replaces one parameter expression with another in an expression tree.
    ///     Needed to combine multiple filter expressions into a single AND chain
    ///     that shares the same parameter.
    /// </summary>
    private sealed class ParameterReplacer(ParameterExpression oldParam, ParameterExpression newParam)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == oldParam ? newParam : base.VisitParameter(node);
    }
}
