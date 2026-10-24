using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Filters;

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
