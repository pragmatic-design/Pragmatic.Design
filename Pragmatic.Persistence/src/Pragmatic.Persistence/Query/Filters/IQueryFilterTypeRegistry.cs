namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Source-generated registry that maps query filter types to their supported entity types.
///     Eliminates GetType().GetInterfaces() reflection in DefaultQueryFilterProvider.
/// </summary>
public interface IQueryFilterTypeRegistry
{
    /// <summary>
    ///     Returns true if the filter type implements IQueryFilter&lt;T&gt; for the given entity type.
    /// </summary>
    bool ImplementsFilterFor(Type filterType, Type entityType);
}
