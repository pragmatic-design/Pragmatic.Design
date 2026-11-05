using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Filters;

/// <summary>The family a filter belongs to: what lifts it, and what values it reads.</summary>
public enum DocFilterFamily : byte
{
    /// <summary>The live documents of a <c>[SoftDelete]</c> entity.</summary>
    SoftDelete,

    /// <summary>The current tenant's documents (<see cref="IDocFilters.TenantId" />).</summary>
    Tenant,

    /// <summary>The documents the caller owns or shares a scope with (<see cref="IDocFilters.UserId" />, <see cref="IDocFilters.Scopes" />).</summary>
    Permission,

    /// <summary>A declared visibility rule: a fixed condition on the document, lifted only by name.</summary>
    Rule,

    /// <summary>The stretches of a <c>[TemporalRelation]</c> entity active now (<see cref="IDocFilters.Now" />): lifted by Raw or by the caller, not by a mode.</summary>
    Temporal,

    /// <summary>A data scope rule computed at read time (<c>DataScopeRule</c>, Computed or Hybrid), OR-ed into the scopes: active for a caller holding its scope.</summary>
    ComputedScope,

    /// <summary>
    ///     A derived type of a hierarchy kept in its root's collection: the documents whose discriminator names it or a type below it. It is what
    ///     the type is, not a filter on what the caller may see: no mode, lift or permission opens it (<see cref="DocFamilies" />).
    /// </summary>
    Type,
}
