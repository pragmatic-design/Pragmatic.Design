namespace Pragmatic.Persistence.Documents.Schema;

/// <summary>
///     Applies the schema the generator describes for a boundary's documents (<c>{Boundary}DocSchema</c>): on MongoDB its indexes
///     (<c>createIndexes</c>, a unique one on the live documents a partial index), on Cosmos DB its containers (partition key, unique keys,
///     composite indexes). Idempotent: a second run on the same declaration reports every element unchanged. What the backend cannot apply is
///     reported as <see cref="DocSchemaOutcome.NotApplied" /> with its reason, never skipped: Cosmos DB's unique keys are fixed when a container is
///     created, hold within a logical partition only, and cannot be limited to the live documents.
/// </summary>
/// <remarks>
///     An index the database has and the schema does not declare is left in place (another writer's, or one an operator made): the schema
///     manages what it declares.
/// </remarks>
public static partial class DocSchema
{
    public static Task<DocSchemaReport> EnsureAsync(DocSession session, IReadOnlyList<DocCollectionSchema> collections, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(collections);
        return session.Cosmos is not null ? EnsureCosmosAsync(session, collections, ct) : EnsureMongoAsync(session, collections, ct);
    }
}
