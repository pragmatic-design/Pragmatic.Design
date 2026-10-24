using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Planning;

/// <summary>
///     The chain as a Cosmos DB query (rendered from the same conditions as the MongoDB filter): its text with <c>@pN</c> parameters, the
///     parameters written from the slots, the partition key's value when the chain names it in an equality, and whether the query needs state
///     across continuations (an ORDER BY, a COUNT, an aggregate: the gateway serves those in one partition only).
/// </summary>
public sealed class DocCosmosQuery(string text, DocJsonWriter? parameters, DocJsonWriter? partition, bool stateful, Func<object?[], string>? pointId = null, int predicateSlot = -1,
    int orderingSlot = -1, (string[] Segments, bool Descending)[]? sortKeys = null)
{
    /// <summary>The slot of the chain's <c>OrderedBy</c> ordering: its <c>ORDER BY</c> replaces the text's <c>{order}</c> marker per call; -1 when it has none.</summary>
    public int OrderingSlot { get; } = orderingSlot;

    /// <summary>The chain's OrderBy keys as the documents hold them (their segments under the root): what a merge across partitions compares.</summary>
    public (string[] Segments, bool Descending)[]? SortKeys { get; } = sortKeys;

    /// <summary>The slot of the chain's <c>Matching</c> predicate (a <see cref="DocPredicate{T}" />); -1 when it has none.</summary>
    public int PredicateSlot { get; } = predicateSlot;

    public string Text { get; } = text;

    /// <summary>The text with the page's OFFSET and LIMIT parameters: built once.</summary>
    public string PagedText => _paged ??= Text + " OFFSET @__skip LIMIT @__take";

    private string? _paged;

    public DocJsonWriter? Parameters { get; } = parameters;

    public DocJsonWriter? Partition { get; } = partition;

    public bool Stateful { get; } = stateful;

    /// <summary>
    ///     The document's id from the slots when the chain names its id and its partition key and nothing else (no ordering): the read is a GET
    ///     of that document (a point read, the cheapest Cosmos DB has), not a query. Null for every other chain.
    /// </summary>
    public Func<object?[], string>? PointId { get; } = pointId;
}
