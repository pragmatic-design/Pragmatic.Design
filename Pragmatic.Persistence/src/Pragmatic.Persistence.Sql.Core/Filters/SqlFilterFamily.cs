namespace Pragmatic.Persistence.Sql.Filters;

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
