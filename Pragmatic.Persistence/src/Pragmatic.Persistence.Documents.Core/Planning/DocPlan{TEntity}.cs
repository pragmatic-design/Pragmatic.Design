using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Planning;

/// <summary>
///     A chain as the generator translated it, for MongoDB: the filter (its conditions, written from the slots each call), the sort, the
///     fields a projection reads (null: the whole document), the slot that holds a Select's delegate (-1: none), the field an aggregate
///     reduces, and the call site it came from (in messages).
/// </summary>
public sealed class DocPlan<TEntity> where TEntity : IDocumentEntity<TEntity>
{
    public DocPlan(string origin, DocFilterWriter? filter, Action<BsonWriter>? sort, string[]? fields, int selectSlot = -1, string? aggregateField = null, DocCosmosQuery? cosmos = null, DocFilterWriter? update = null, Action<BsonWriter>? group = null, Func<BsonDocument, object>? groupRead = null, DocFilterWriter? having = null, Action<BsonWriter>? groupSort = null, string? unwind = null, int predicateSlot = -1, int orderingSlot = -1, DocPatchWriter<TEntity>? cosmosUpdate = null, bool updatePipeline = false)
    {
        CosmosUpdate = cosmosUpdate;
        UpdatePipeline = updatePipeline;
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

    /// <summary>
    ///     True when <see cref="Update" /> is the <c>$set</c> stage of an update pipeline (a value computed from the document's own fields, which
    ///     only an aggregation expression reads): the update's <c>u</c> is then an array of that one stage.
    /// </summary>
    public bool UpdatePipeline { get; }

    /// <summary>
    ///     The same update as Cosmos DB's patch operations, one document at a time (Cosmos DB has no update by filter); null when it has none
    ///     (a delete, or more fields than a patch takes).
    /// </summary>
    public DocPatchWriter<TEntity>? CosmosUpdate { get; }

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
