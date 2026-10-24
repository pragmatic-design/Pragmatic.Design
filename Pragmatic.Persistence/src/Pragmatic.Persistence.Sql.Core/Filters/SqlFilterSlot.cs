namespace Pragmatic.Persistence.Sql.Filters;

/// <summary>
///     One filter a statement applies to one entity, declared by the generated plan: the entity, the family, the filter class a caller lifts
///     it by (<c>Reservation.OwnershipFilter</c>), the name a named lift uses and the permission that lets a caller past it. The same entity
///     read twice in a statement has one slot per filter: a lift is decided per entity, not per position.
/// </summary>
public sealed class SqlFilterSlot(Type entity, SqlFilterFamily family, Type? filter, string? name, string? bypass = null)
{
    public Type Entity { get; } = entity;

    public SqlFilterFamily Family { get; } = family;

    /// <summary>The filter class (Pragmatic's nested <c>SoftDeleteFilter</c>, <c>TenantFilter</c>, <c>OwnershipFilter</c>…); null for a rule.</summary>
    public Type? Filter { get; } = filter;

    /// <summary>The name a named lift uses (<c>SoftDelete</c>, <c>Tenant</c>, a rule's <c>Visibility:{rule}</c>); null when none lifts it by name.</summary>
    public string? Name { get; } = name;

    /// <summary>The permission that lets a caller past the filter (<c>{boundary}.{entity}.view-all</c>); null when none does.</summary>
    public string? Bypass { get; } = bypass;
}
