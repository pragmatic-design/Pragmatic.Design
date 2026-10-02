using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Query.Interfaces;

namespace Pragmatic.Persistence.EFCore.Query;

/// <summary>
///     The sets a declared join reads, with the filters of their entity applied before the join.
/// </summary>
/// <remarks>
///     <para>
///         A joined set is not a navigation: the visitor that filters collections never sees it, and the
///         root filter is applied to the root's entity only. Unfiltered here, a <c>[Join&lt;T&gt;]</c> would
///         read every row of <c>T</c> the model filters do not already hide — an owned row, a row outside the
///         caller's scopes, anything a registered <see cref="IQueryFilter" /> withholds on its own.
///     </para>
///     <para>
///         The predicate is the one the provider composes for the entity in
///         <see cref="FilterScope.Joins" />, in the same <see cref="FilterContext" /> the rest of the
///         query is filtered in — typed, so no <c>Where</c> is built at runtime.
///     </para>
/// </remarks>
/// <param name="inner">The sets as the boundary's context holds them.</param>
/// <param name="filters">The provider every other part of the query is filtered by.</param>
/// <param name="context">The context of this execution.</param>
internal sealed class FilteredJoinSources(IJoinSourceProvider inner, IQueryFilterProvider filters, FilterContext context)
    : IJoinSourceProvider
{
    /// <inheritdoc />
    public IJoinSources ForBoundary<TBoundary>() where TBoundary : class
        => new Sets(inner.ForBoundary<TBoundary>(), filters, context);

    private sealed class Sets(IJoinSources inner, IQueryFilterProvider filters, FilterContext context) : IJoinSources
    {
        public IQueryable<TEntity> Of<
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
                System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties
                | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
            TEntity>() where TEntity : class, IEntity
        {
            var set = inner.Of<TEntity>();
            var predicate = filters.GetCombinedFilter<TEntity>(context, NavigationContext.ForJoin(typeof(TEntity)));
            return predicate is null ? set : set.Where(predicate);
        }
    }
}
