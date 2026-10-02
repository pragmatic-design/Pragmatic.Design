namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Default passthrough implementation of <see cref="IQueryFilterTypeRegistry" />.
///     Accepts all filter-entity combinations (conservative: may apply filters broadly).
///     The SG-generated implementation provides accurate compile-time mapping.
/// </summary>
public sealed class PassthroughQueryFilterTypeRegistry : IQueryFilterTypeRegistry
{
    /// <summary>
    ///     Returns true for all filter+entity pairs. This is a safe default because
    ///     <see cref="DefaultQueryFilterProvider" /> will attempt to cast each filter
    ///     to <c>IQueryFilter&lt;T&gt;</c> at query time — non-matching filters are
    ///     skipped by the cast check. The SG-generated registry provides precise mapping.
    /// </summary>
    public bool ImplementsFilterFor(Type filterType, Type entityType) => true;
}
