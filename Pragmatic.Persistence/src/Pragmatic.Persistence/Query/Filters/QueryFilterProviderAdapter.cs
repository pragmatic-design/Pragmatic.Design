using System.Linq.Expressions;

namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Puts the registered <see cref="IQueryFilter" />s into the navigation map, so a filter guarding a
///     root query guards a collection navigation too.
/// </summary>
/// <remarks>
///     <para>
///         <b>It composes nothing.</b> For each entity it asks
///         <see cref="IQueryFilterProvider.GetCombinedFilter(Type, FilterContext, NavigationContext?)" />
///         for the same predicate the root of a query would get, and stores that. Which filters apply,
///         whether an anonymous caller bypasses the permission ones, whether a permission filter with
///         no user fails the query closed, and how the pieces fit together — all of it is decided in
///         the provider, once.
///     </para>
///     <para>
///         ⚠️ Deciding those things here would mean reimplementing the composition, and a simple one
///         can only AND. Ownership is restrictive and ANDs correctly, but data scopes are
///         <b>additive</b>: the root ORs them together and only then ANDs the result with soft-delete
///         and tenant, so a row is visible if it matches <em>any</em> scope. ANDing two scopes is
///         strictly narrower — rows visible on a root query would vanish from an <c>Include</c>.
///         Asking the provider instead of reimplementing it is what lets ownership and scopes reach
///         navigations with the same meaning they have at the root.
///     </para>
///     <para>
///         The context describes a <b>collection</b>, which is the only navigation the map is ever
///         applied to: <c>PragmaticQueryFilterVisitor</c> rewrites collection navigations and leaves
///         references untouched — and it says where the query reads it, because the visitor asks for
///         one map per position. Saying so is what makes <see cref="FilterScope" /> mean something
///         here: a filter that leaves out <see cref="FilterScope.Projections" /> is in the map for an
///         <c>Include</c> and not in the one for a <c>Select</c>.
///     </para>
/// </remarks>
public sealed class QueryFilterProviderAdapter : IVisibilityFilterProvider
{
    private static readonly IReadOnlyDictionary<Type, LambdaExpression> EmptyMap =
        new Dictionary<Type, LambdaExpression>();

    private readonly IReadOnlyList<Type> _entityTypes;
    private readonly IQueryFilterProvider _provider;

    /// <summary>Creates the adapter over every filter the container holds.</summary>
    /// <param name="filters">The registered filters; they name the entities worth asking about.</param>
    /// <param name="provider">The provider that decides what applies, and composes it.</param>
    /// <remarks>
    ///     The filters are read for their <see cref="IQueryFilter.EntityType" /> only. Nothing else
    ///     here knows which entities a container can filter, and enumerating them once is cheaper than
    ///     asking the provider about every entity in the model.
    /// </remarks>
    public QueryFilterProviderAdapter(IEnumerable<IQueryFilter> filters, IQueryFilterProvider provider)
    {
        Ensure.Ensure.ThrowIfNull(filters);
        _provider = Ensure.Ensure.ThrowIfNull(provider);

        _entityTypes = [.. filters
            .Select(static f => f.EntityType)
            .Where(static t => t is not null)
            .Distinct()
            .Cast<Type>()];
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<Type, LambdaExpression> GetFilters(FilterContext context)
        => GetFilters(context, FilterScope.Collections);

    /// <inheritdoc />
    /// <remarks>
    ///     The position goes to the provider in the context, and each filter's <see cref="IQueryFilter.Scope" />
    ///     answers for it there — the same place the root is decided.
    /// </remarks>
    public IReadOnlyDictionary<Type, LambdaExpression> GetFilters(FilterContext context, FilterScope position)
    {
        Ensure.Ensure.ThrowIfNull(context);

        if (_entityTypes.Count == 0)
            return EmptyMap;

        Dictionary<Type, LambdaExpression>? map = null;

        foreach (var entity in _entityTypes)
        {
            var predicate = _provider.GetCombinedFilter(
                entity, context, NavigationContext.ForCollection(entity, position));

            if (predicate is null)
                continue;

            map ??= [];
            map[entity] = predicate;
        }

        return map ?? EmptyMap;
    }
}
