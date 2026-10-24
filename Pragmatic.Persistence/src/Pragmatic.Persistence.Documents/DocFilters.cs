using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

/// <summary>What one filter of a read does in one execution.</summary>
public enum DocFilterState : byte
{
    /// <summary>The filter's condition is in the read.</summary>
    Active = 0,

    /// <summary>The filter is lifted: its condition is left out.</summary>
    Lifted = 1,

    /// <summary>No document of the entity is visible (an anonymous caller where a permission filter applies and fails closed).</summary>
    Closed = 2,
}

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
}

/// <summary>
///     One filter of an entity: the entity, the family, the filter class a caller lifts it by (<c>Reservation.OwnershipFilter</c>), the name a
///     named lift uses and the permission that lets a caller past it. The Sql engine's <c>SqlFilterSlot</c>, for documents.
/// </summary>
public sealed class DocFilterSlot(Type entity, DocFilterFamily family, Type? filter, string? name, string? bypass = null)
{
    public Type Entity { get; } = entity;

    public DocFilterFamily Family { get; } = family;

    public Type? Filter { get; } = filter;

    public string? Name { get; } = name;

    public string? Bypass { get; } = bypass;
}

/// <summary>
///     Who is asking and what they lifted, for the filters of Pragmatic's entities: the state of each filter and the values the active ones read.
///     Without one (<see cref="DocSession.Filters" /> null) every filter is active and every value empty: a tenant, owner or scope filter then
///     matches nothing.
/// </summary>
public interface IDocFilters
{
    DocFilterState StateOf(DocFilterSlot slot);

    /// <summary>The current tenant; null when none is resolved (a tenant filter then matches nothing).</summary>
    string? TenantId { get; }

    /// <summary>The current user's identifier; null when anonymous.</summary>
    string? UserId { get; }

    /// <summary>The access scopes the current user holds.</summary>
    IReadOnlyCollection<string> Scopes { get; }

    /// <summary>The instant the read is at, the caller's clock (<c>FilterContext.Now</c>): what a temporal filter compares against.</summary>
    DateTimeOffset Now { get; }
}

/// <summary>
///     One filter of a Pragmatic entity as the generator wrote it: its slot, its condition for MongoDB (written into an open document from the
///     caller's values), its condition for Cosmos DB (a text whose parameters are <c>@__tenant</c>, <c>@__user</c>, <c>@__scopes</c> or its own
///     <c>@__rN</c>, written by <see cref="CosmosParameters" />), and whether it names the tenant the entity is partitioned by on Cosmos DB.
/// </summary>
public sealed class DocFamily(DocFilterSlot slot, Action<BsonWriter, IDocFilters?> mongo, string cosmos, Action<System.Text.Json.Utf8JsonWriter, IDocFilters?>? cosmosParameters, bool partitionsByTenant = false,
    Func<DocSession, IDocFilters?, CancellationToken, Task<IReadOnlyList<Guid>>>? visibleParents = null)
{
    public DocFilterSlot Slot { get; } = slot;

    /// <summary>
    ///     A trait's child: the keys of the parents the caller may see, read before each execution (<see cref="DocFamilies.PrepareAsync{TEntity}" />)
    ///     because a document store's filter cannot read another collection; the conditions then match the child's foreign key against them.
    /// </summary>
    public Func<DocSession, IDocFilters?, CancellationToken, Task<IReadOnlyList<Guid>>>? VisibleParents { get; } = visibleParents;

    public Action<BsonWriter, IDocFilters?> Mongo { get; } = mongo;

    public string Cosmos { get; } = cosmos;

    public Action<System.Text.Json.Utf8JsonWriter, IDocFilters?>? CosmosParameters { get; } = cosmosParameters;

    /// <summary>True for the tenant of an entity partitioned by it: an active tenant filter is the read's partition.</summary>
    public bool PartitionsByTenant { get; } = partitionsByTenant;
}
