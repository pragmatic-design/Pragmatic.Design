using System.Linq.Expressions;

namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Provides query filters for a given entity type.
///     Used by executors to apply global filters.
/// </summary>
public interface IQueryFilterProvider
{
    /// <summary>
    ///     Gets all applicable filters for the given entity type.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <returns>Filters ordered by priority.</returns>
    IEnumerable<IQueryFilter<T>> GetFilters<T>() where T : class;

    /// <summary>
    ///     Gets a combined filter expression for the given entity type.
    ///     All filters are AND-ed together.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="context">The navigation context (null for root).</param>
    /// <returns>Combined predicate or null if no filters apply.</returns>
    Expression<Func<T, bool>>? GetCombinedFilter<T>(NavigationContext? context = null) where T : class;

    /// <summary>
    ///     Gets a combined filter expression for the given entity type,
    ///     respecting <see cref="FilterContext"/> for mode-based and selective disabling.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="filterContext">The filter context with mode, disabled filters, and tenant info.</param>
    /// <param name="navigationContext">The navigation context (null for root).</param>
    /// <returns>Combined predicate or null if no filters apply.</returns>
    Expression<Func<T, bool>>? GetCombinedFilter<T>(FilterContext filterContext, NavigationContext? navigationContext = null) where T : class
        => GetCombinedFilter<T>(navigationContext); // DIM: backward compatible

    /// <summary>
    ///     The context a read carries when the caller composes none: the mode and the lifts in scope.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A caller that builds its own <see cref="FilterContext" /> from nothing loses whatever the
    ///     scope asked for. <c>Disable&lt;T&gt;()</c> survived that only because the default provider
    ///     consults the toggle directly; <c>UseMode</c> lives in the context alone, so it reached the
    ///     executor and no hand-composed read. Ask for this and add to it, rather than starting blank.
    /// </remarks>
    /// <remarks>
    ///     The default implementation has nothing injected to read the time from, so it says which
    ///     clock it uses instead of leaving it to a property default. An implementation that can be
    ///     given one — <c>DefaultQueryFilterProvider</c> is — takes a <see cref="TimeProvider" /> and
    ///     can be pinned.
    /// </remarks>
    FilterContext AmbientFilterContext() => FilterContext.At(TimeProvider.System);

    /// <summary>
    ///     Checks if any filters exist for the given entity type.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <returns>True if filters exist.</returns>
    bool HasFilters<T>() where T : class;

    /// <summary>
    ///     The combined predicate for an entity type, as a <see cref="LambdaExpression" />.
    /// </summary>
    /// <param name="entityType">The entity to filter.</param>
    /// <param name="filterContext">The mode, the disabled filters, the user and the tenant.</param>
    /// <param name="navigationContext">Where this predicate is about to be applied.</param>
    /// <remarks>
    ///     <para>
    ///         The same answer as <c>GetCombinedFilter&lt;T&gt;</c>, reachable without a generic
    ///         parameter. It exists so the navigation map can be built by <b>asking</b> rather than by
    ///         reimplementing: composing the filters for an entity is more than ANDing them — ownership
    ///         is restrictive, scopes are additive and OR together, an anonymous caller bypasses
    ///         permission filters, and a permission filter with no user fails the whole query closed.
    ///         Written twice, those rules drift, and the drift shows up as rows visible on a root query
    ///         disappearing from an <c>Include</c>.
    ///     </para>
    /// </remarks>
    LambdaExpression? GetCombinedFilter(
        Type entityType, FilterContext filterContext, NavigationContext? navigationContext = null);
}
