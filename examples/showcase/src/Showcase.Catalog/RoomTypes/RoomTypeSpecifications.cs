namespace Showcase.Catalog.Entities;

/// <summary>The rules that select room types.</summary>
/// <remarks>
///     A specification rather than a LINQ chain on <c>Query()</c>: every persistence engine reads a specification, and only EF Core serves an
///     <c>IQueryable</c>.
/// </remarks>
public static partial class RoomTypeSpecifications
{
    /// <summary>
    ///     The room types of one property among the ids asked for: one read for a whole batch. ⚠️ <c>PersistenceId</c>, not <c>Id</c>: <c>Id</c>
    ///     is the entity's convenience face and no engine can translate it.
    /// </summary>
    public static Specification<RoomType> OfPropertyAmong(Guid propertyId, IReadOnlyCollection<Guid> roomTypeIds)
        => Spec<RoomType>.Where(rt => rt.PropertyId == propertyId && roomTypeIds.Contains(rt.PersistenceId));
}
