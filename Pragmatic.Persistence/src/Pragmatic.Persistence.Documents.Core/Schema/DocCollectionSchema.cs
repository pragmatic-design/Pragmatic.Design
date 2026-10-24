namespace Pragmatic.Persistence.Documents.Schema;

/// <summary>
///     A collection as the generator describes it from the entity: its name, its partition key path on Cosmos DB (<c>/TenantId</c>, <c>/id</c>),
///     its indexes, and the composite indexes its declared orderings need on Cosmos DB (each the paths and their orders).
/// </summary>
public sealed record DocCollectionSchema(
    string Collection,
    string PartitionKeyPath,
    IReadOnlyList<DocIndex> Indexes,
    IReadOnlyList<(string Path, bool Descending)[]> Composites);
