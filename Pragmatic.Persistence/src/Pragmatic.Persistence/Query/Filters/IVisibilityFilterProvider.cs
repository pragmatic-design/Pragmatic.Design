using System.Linq.Expressions;

namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Provides dynamic visibility filters at runtime.
///     Implementations are composed into the <see cref="FilterMap"/> by the FilterMapComposer.
/// </summary>
/// <remarks>
///     <para>
///         Visibility filters are row-level security filters that depend on the current user/context.
///         Unlike SG-generated static filters (SoftDelete, Tenant), these are resolved at runtime.
///     </para>
///     <para>
///         The existing <see cref="IQueryFilterProvider"/> is adapted to this interface
///         via <c>QueryFilterProviderAdapter</c> for backward compatibility.
///     </para>
/// </remarks>
public interface IVisibilityFilterProvider
{
    /// <summary>
    ///     Returns dynamic filters for the current context.
    ///     Keys are entity types, values are <c>Expression&lt;Func&lt;T, bool&gt;&gt;</c>.
    /// </summary>
    /// <param name="context">The current filter context (user, tenant, mode).</param>
    /// <returns>
    ///     Dictionary of entity type → filter expression.
    ///     Empty dictionary if no dynamic filters apply.
    /// </returns>
    IReadOnlyDictionary<Type, LambdaExpression> GetFilters(FilterContext context);

    /// <summary>
    ///     The filters for a collection read in <paramref name="position" />:
    ///     <see cref="FilterScope.Collections" />, <see cref="FilterScope.Subqueries" /> or
    ///     <see cref="FilterScope.Projections" />.
    /// </summary>
    /// <remarks>
    ///     A provider whose filters apply wherever a collection is read has nothing to add, and answers
    ///     <see cref="GetFilters(FilterContext)" /> for every position.
    /// </remarks>
    IReadOnlyDictionary<Type, LambdaExpression> GetFilters(FilterContext context, FilterScope position)
        => GetFilters(context);
}
