namespace Pragmatic.Persistence.Sql;

/// <summary>What one filter of a statement does in one execution.</summary>
public enum SqlFilterState : byte
{
    /// <summary>The filter's predicate is in the statement.</summary>
    Active = 0,

    /// <summary>The filter is lifted: its predicate is left out.</summary>
    Lifted = 1,

    /// <summary>The entity's rows are not visible at all (an anonymous caller where a permission filter applies and fails closed).</summary>
    Closed = 2,
}

/// <summary>The family a filter belongs to: what lifts it, and what values it reads.</summary>
public enum SqlFilterFamily : byte
{
    /// <summary>The live rows of a <c>[SoftDelete]</c> entity.</summary>
    SoftDelete,

    /// <summary>The current tenant's rows (<see cref="ISqlFilters.TenantId" />).</summary>
    Tenant,

    /// <summary>The rows the caller owns or shares a scope with (<see cref="ISqlFilters.UserId" />, <see cref="ISqlFilters.Scopes" />).</summary>
    Permission,

    /// <summary>A declared visibility rule: a fixed predicate on the row, lifted only by name.</summary>
    Rule,

    /// <summary>The stretches of a <c>[TemporalRelation]</c> entity active now (<see cref="ISqlFilters.Now" />): lifted by Raw or by the caller, not by a mode.</summary>
    Temporal,

    /// <summary>A data scope rule computed at read time (<c>DataScopeRule</c>, Computed or Hybrid), OR-ed into the scopes: active for a caller holding its scope.</summary>
    ComputedScope,
}

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

/// <summary>
///     Who is asking and what they lifted, for the filters of Pragmatic's entities: the state of each filter of a statement, and the values
///     the active ones read. The statement's text is chosen by the states (one variant per combination, assembled once), and the values are
///     its parameters. Without one (<see cref="SqlSession.Filters" /> null) every filter is active and every value empty: a tenant, owner or
///     scope filter then matches nothing.
/// </summary>
public interface ISqlFilters
{
    /// <summary>The state of <paramref name="slot" /> for the execution about to run.</summary>
    SqlFilterState StateOf(SqlFilterSlot slot);

    /// <summary>The current tenant; null when none is resolved (a tenant filter then matches nothing).</summary>
    string? TenantId { get; }

    /// <summary>The current user's identifier; null when anonymous.</summary>
    string? UserId { get; }

    /// <summary>The access scopes the current user holds.</summary>
    IReadOnlyCollection<string> Scopes { get; }

    /// <summary>The instant the execution reads at, the caller's clock (<c>FilterContext.Now</c>): what a temporal filter compares against.</summary>
    DateTimeOffset Now { get; }
}
