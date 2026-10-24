namespace Pragmatic.Persistence.Documents;

/// <summary>
///     An index a collection declares: its name, its fields in order (the stored names), whether it is unique, and the field a unique one is
///     limited to the live documents by (a soft delete's <c>IsDeleted</c>: the index holds the documents where it is false), or null.
/// </summary>
public sealed record DocIndex(string Name, IReadOnlyList<string> Fields, bool Unique = false, string? LiveField = null);

/// <summary>
///     A collection as the generator describes it from the entity: its name, its partition key path on Cosmos DB (<c>/TenantId</c>, <c>/id</c>),
///     its indexes, and the composite indexes its declared orderings need on Cosmos DB (each the paths and their orders).
/// </summary>
public sealed record DocCollectionSchema(
    string Collection,
    string PartitionKeyPath,
    IReadOnlyList<DocIndex> Indexes,
    IReadOnlyList<(string Path, bool Descending)[]> Composites);

/// <summary>What <see cref="DocSchema.EnsureAsync" /> did with one element of a collection's schema.</summary>
public enum DocSchemaOutcome
{
    Created,
    Updated,
    Unchanged,

    /// <summary>Declared and not applied, the reason said: the backend cannot do it as declared (never skipped in silence).</summary>
    NotApplied,
}

/// <summary>One element of the schema (a collection, an index, a unique key, the indexing policy) and its outcome.</summary>
public sealed record DocSchemaChange(string Collection, string Element, DocSchemaOutcome Outcome, string? Reason = null);

/// <summary>Everything <see cref="DocSchema.EnsureAsync" /> did, element by element.</summary>
public sealed class DocSchemaReport(IReadOnlyList<DocSchemaChange> changes)
{
    public IReadOnlyList<DocSchemaChange> Changes { get; } = changes;

    /// <summary>True when the run created or changed nothing: the database held the schema already.</summary>
    public bool Unchanged => Changes.All(c => c.Outcome is DocSchemaOutcome.Unchanged or DocSchemaOutcome.NotApplied);

    /// <summary>The declared elements the backend could not apply, each with its reason.</summary>
    public IReadOnlyList<DocSchemaChange> NotApplied => [.. Changes.Where(c => c.Outcome == DocSchemaOutcome.NotApplied)];
}

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
