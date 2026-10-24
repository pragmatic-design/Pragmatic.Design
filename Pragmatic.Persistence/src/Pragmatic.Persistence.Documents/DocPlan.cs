using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

/// <summary>Writes a filter's conditions (the elements of the filter document) from the chain's slots.</summary>
public delegate void DocFilterWriter(BsonWriter writer, ReadOnlySpan<object?> slots);

/// <summary>Writes JSON from the chain's slots: a Cosmos query's parameters (the elements of its array), or its partition key's value.</summary>
public delegate void DocJsonWriter(System.Text.Json.Utf8JsonWriter writer, ReadOnlySpan<object?> slots);

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

/// <summary>
///     A chain as the generator translated it, for MongoDB: the filter (its conditions, written from the slots each call), the sort, the
///     fields a projection reads (null: the whole document), the slot that holds a Select's delegate (-1: none), the field an aggregate
///     reduces, and the call site it came from (in messages).
/// </summary>
public sealed class DocPlan<TEntity> where TEntity : IDocumentEntity<TEntity>
{
    public DocPlan(string origin, DocFilterWriter? filter, Action<BsonWriter>? sort, string[]? fields, int selectSlot = -1, string? aggregateField = null, DocCosmosQuery? cosmos = null, DocFilterWriter? update = null, Action<BsonWriter>? group = null, Func<BsonDocument, object>? groupRead = null, DocFilterWriter? having = null, Action<BsonWriter>? groupSort = null, string? unwind = null, int predicateSlot = -1, int orderingSlot = -1)
    {
        PredicateSlot = predicateSlot;
        OrderingSlot = orderingSlot;
        Unwind = unwind;
        Group = group;
        Having = having;
        GroupSort = groupSort;
        GroupRead = groupRead;
        Origin = origin;
        Filter = filter;
        Sort = sort;
        Fields = fields;
        SelectSlot = selectSlot;
        AggregateField = aggregateField;
        Cosmos = cosmos;
        Update = update;
    }

    /// <summary>A grouped Select's <c>$group</c> stage (its <c>_id</c> and accumulators); null for every other chain.</summary>
    public Action<BsonWriter>? Group { get; }

    /// <summary>The embedded list a SelectMany flattens (its field's path); null for every other chain.</summary>
    public string? Unwind { get; }

    /// <summary>The conditions on the groups after the grouped Select (HAVING): a <c>$match</c> after <c>$group</c>; null when there are none.</summary>
    public DocFilterWriter? Having { get; }

    /// <summary>The groups' ordering by the Select's members: a <c>$sort</c> after <c>$group</c> (and HAVING); null when they have none.</summary>
    public Action<BsonWriter>? GroupSort { get; }

    /// <summary>Builds the Select's object from one <c>$group</c> result document.</summary>
    public Func<BsonDocument, object>? GroupRead { get; }

    /// <summary>An ExecuteUpdateAsync's operators (the elements of the update document: <c>$set</c>, and <c>$inc</c> of a version), from the slots.</summary>
    public DocFilterWriter? Update { get; }

    /// <summary>The Cosmos DB query of the same chain.</summary>
    public DocCosmosQuery? Cosmos { get; }

    public string Origin { get; }

    public DocFilterWriter? Filter { get; }

    public Action<BsonWriter>? Sort { get; }

    public string[]? Fields { get; }

    public int SelectSlot { get; }

    public string? AggregateField { get; }

    /// <summary>
    ///     The filter document under <paramref name="name" /> (<c>filter</c>, <c>query</c>, <c>$match</c>): empty when the chain has none. A Pragmatic
    ///     entity's active families join the chain's conditions under <c>$and</c> (<see cref="DocFamilies" />).
    /// </summary>
    internal void WriteFilter(BsonWriter w, string name, ReadOnlySpan<object?> slots, DocSession session)
    {
        w.StartDocument(name);
        if (!DocFamilies.WriteMongo<TEntity>(w, session.Filters, Filter, slots, PredicateOf(slots)))
            Filter?.Invoke(w, slots);
        w.EndDocument();
    }

    /// <summary>The slot of the chain's <c>Matching</c> predicate; -1 when it has none.</summary>
    public int PredicateSlot { get; }

    /// <summary>The chain's <c>Matching</c> predicate, from its slot; null when it has none.</summary>
    internal DocPredicate<TEntity>? PredicateOf(ReadOnlySpan<object?> slots) => PredicateSlot >= 0 ? (DocPredicate<TEntity>?)slots[PredicateSlot] : null;

    /// <summary>The slot of the chain's <c>OrderedBy</c> ordering (a <see cref="DocOrdering{T}" />); -1 when it has none.</summary>
    public int OrderingSlot { get; }

    /// <summary>True when the chain orders: by its OrderBy (<see cref="Sort" />) or by an ordering composed at runtime.</summary>
    internal bool Sorts => Sort is not null || OrderingSlot >= 0;

    /// <summary>The sort document's elements: the runtime ordering's, from its slot, or the chain's OrderBy.</summary>
    internal void WriteSort(BsonWriter w, ReadOnlySpan<object?> slots)
    {
        if (OrderingSlot >= 0)
            ((DocOrdering<TEntity>)slots[OrderingSlot]!).WriteMongo(w);
        else
            Sort?.Invoke(w);
    }

    internal void WriteProjection(BsonWriter w)
    {
        if (Fields is null)
            return;
        w.StartDocument("projection");
        foreach (var field in Fields)
            w.Write(field, 1);
        w.EndDocument();
    }
}
