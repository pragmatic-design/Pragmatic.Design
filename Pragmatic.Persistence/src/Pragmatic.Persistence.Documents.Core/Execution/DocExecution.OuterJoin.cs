using Pragmatic.Persistence.Documents.Mongo.Bson;
using Pragmatic.Persistence.Documents.Planning;

namespace Pragmatic.Persistence.Documents.Execution;

/// <summary>
///     A LeftJoin, a RightJoin or a FullJoin of two collections, each row built by the result the generator wrote (the side that is missing
///     null). MongoDB: a LeftJoin is the Join's <c>aggregate</c> with <c>$unwind</c> keeping a document without a match; a RightJoin the same
///     from the inner collection, the outer documents looked up with the outer chain's <c>$match</c>; a FullJoin the LeftJoin's rows, then, by
///     <c>$unionWith</c>, the inner documents no outer one matches. Cosmos DB has no join between containers: both sides are read, the outer as
///     its chain says and the inner by the outer keys (a LeftJoin) or whole (a RightJoin, a FullJoin), and joined here. Skip and Take page the
///     joined rows.
/// </summary>
public static partial class DocExecution
{
    /// <summary>The field a FullJoin's inner document without a match is under, in the rows <c>$unionWith</c> adds.</summary>
    private const string Alone = "__alone";

    private static async ValueTask<List<TResult>> MongoOuterJoinAsync<TOuter, TInner, TResult>(DocQuery<TResult> query, DocPlan<TOuter> plan, DocJoin<TInner> join, DocQuery<TInner> inner,
        CancellationToken ct)
        where TOuter : IDocumentEntity<TOuter>
        where TInner : IDocumentEntity<TInner>
    {
        var session = query.Session;
        var slots = query.SlotArray;
        var innerSlots = inner.SlotArray;
        var fromInner = join.Kind == DocJoinKind.Right;
        var reply = await session.RunAsync(w =>
        {
            w.Write("aggregate", fromInner ? TInner.Collection : TOuter.Collection);
            w.StartArray("pipeline");
            var stage = fromInner
                ? FromInner(w, 0, plan, join, session, slots, innerSlots, unmatchedOnly: false)
                : FromOuter(w, 0, plan, join, session, slots, innerSlots);
            if (join.Kind == DocJoinKind.Full)
            {
                w.StartDocument(stage++);
                w.StartDocument("$unionWith");
                w.Write("coll", TInner.Collection);
                w.StartArray("pipeline");
                var inside = FromInner(w, 0, plan, join, session, slots, innerSlots, unmatchedOnly: true);
                w.StartDocument(inside);
                w.StartDocument("$replaceRoot");
                w.StartDocument("newRoot");
                w.Write(Alone, "$$ROOT");
                w.EndDocument();
                w.EndDocument();
                w.EndDocument();
                w.EndArray();
                w.EndDocument();
                w.EndDocument();
            }

            if (query.SkipCount > 0)
            {
                w.StartDocument(stage++);
                w.Write("$skip", (long)query.SkipCount);
                w.EndDocument();
            }

            if (query.TakeCount >= 0)
            {
                w.StartDocument(stage);
                w.Write("$limit", (long)query.TakeCount);
                w.EndDocument();
            }

            w.EndArray();
            w.StartDocument("cursor");
            w.EndDocument();
        }, ct).ConfigureAwait(false);

        var build = join.Result!;
        var documents = await session.Cursor(reply).ToListAsync(ct).ConfigureAwait(false);
        var results = new List<TResult>(documents.Count);
        foreach (var document in documents)
        {
            object? outer;
            object? matched;
            if (document.TryGetDocument(Alone, out var alone))
            {
                outer = null;
                matched = TInner.ReadDocument(alone.Bytes.Span);
            }
            else if (fromInner)
            {
                outer = document.TryGetDocument(Joined, out var found) ? TOuter.ReadDocument(found.Bytes.Span) : null;
                matched = TInner.ReadDocument(document.Bytes.Span); // the inner reader skips the joined field
            }
            else
            {
                outer = TOuter.ReadDocument(document.Bytes.Span); // the outer reader skips the joined field
                matched = document.TryGetDocument(Joined, out var found) ? TInner.ReadDocument(found.Bytes.Span) : null;
            }

            results.Add((TResult)build(outer, matched));
        }

        return results;
    }

    /// <summary>The stages from the outer collection: its <c>$match</c> and <c>$sort</c>, the inner documents looked up, each kept without one; the next stage's index.</summary>
    private static int FromOuter<TOuter, TInner>(BsonWriter w, int stage, DocPlan<TOuter> plan, DocJoin<TInner> join, DocSession session, object?[] slots, object?[] innerSlots)
        where TOuter : IDocumentEntity<TOuter>
        where TInner : IDocumentEntity<TInner>
    {
        w.StartDocument(stage++);
        plan.WriteFilter(w, "$match", slots, session);
        w.EndDocument();
        if (plan.Sort is not null)
        {
            w.StartDocument(stage++);
            w.StartDocument("$sort");
            plan.Sort(w);
            w.EndDocument();
            w.EndDocument();
        }

        w.StartDocument(stage++);
        w.StartDocument("$lookup");
        w.Write("from", TInner.Collection);
        w.Write("localField", join.LocalField);
        w.Write("foreignField", join.ForeignField);
        w.StartArray("pipeline");
        w.StartDocument(0);
        w.StartDocument("$match");
        if (!DocFamilies.WriteMongo<TInner>(w, session.Filters, join.Filter, innerSlots)) // the joined side's families too
            join.Filter?.Invoke(w, innerSlots);
        w.EndDocument();
        w.EndDocument();
        w.EndArray();
        w.Write("as", Joined);
        w.EndDocument();
        w.EndDocument();
        Unwind(w, stage++);
        return stage;
    }

    /// <summary>
    ///     The stages from the inner collection: its <c>$match</c>, the outer documents looked up with the outer chain's <c>$match</c>, then each
    ///     inner document kept without one, or (<paramref name="unmatchedOnly" />) only those; the next stage's index.
    /// </summary>
    private static int FromInner<TOuter, TInner>(BsonWriter w, int stage, DocPlan<TOuter> plan, DocJoin<TInner> join, DocSession session, object?[] slots, object?[] innerSlots,
        bool unmatchedOnly)
        where TOuter : IDocumentEntity<TOuter>
        where TInner : IDocumentEntity<TInner>
    {
        w.StartDocument(stage++);
        w.StartDocument("$match");
        if (!DocFamilies.WriteMongo<TInner>(w, session.Filters, join.Filter, innerSlots))
            join.Filter?.Invoke(w, innerSlots);
        w.EndDocument();
        w.EndDocument();

        w.StartDocument(stage++);
        w.StartDocument("$lookup");
        w.Write("from", TOuter.Collection);
        w.Write("localField", join.ForeignField);
        w.Write("foreignField", join.LocalField);
        w.StartArray("pipeline");
        w.StartDocument(0);
        plan.WriteFilter(w, "$match", slots, session); // the outer chain's conditions and families pick the documents that may match
        w.EndDocument();
        w.EndArray();
        w.Write("as", Joined);
        w.EndDocument();
        w.EndDocument();

        if (!unmatchedOnly)
        {
            Unwind(w, stage++);
            return stage;
        }

        w.StartDocument(stage++);
        w.StartDocument("$match");
        w.StartDocument(Joined);
        w.Write("$size", 0);
        w.EndDocument();
        w.EndDocument();
        w.EndDocument();
        return stage;
    }

    /// <summary>One row per joined document, and a row without the field for a document that has none.</summary>
    private static void Unwind(BsonWriter w, int stage)
    {
        w.StartDocument(stage);
        w.StartDocument("$unwind");
        w.Write("path", "$" + Joined);
        w.Write("preserveNullAndEmptyArrays", true);
        w.EndDocument();
        w.EndDocument();
    }

    private static async ValueTask<List<TResult>> CosmosOuterJoinAsync<TOuter, TInner, TKey, TResult>(DocQuery<TResult> query, DocPlan<TOuter> plan, DocJoin<TInner> join, DocQuery<TInner> inner,
        Func<TOuter, TKey> outerKey, Func<TInner, TKey> innerKey, CancellationToken ct)
        where TOuter : IDocumentEntity<TOuter>
        where TInner : IDocumentEntity<TInner>
        where TKey : notnull
    {
        var outers = await CosmosListAsync(query.Unpaged<TOuter>(), plan, ct).ConfigureAwait(false); // the outer documents whole: the paging is the joined rows'
        var keys = new HashSet<TKey>();
        foreach (var outer in outers)
            keys.Add(outerKey(outer));

        // A LeftJoin needs the inner documents of the outer keys; a RightJoin and a FullJoin every one the inner chain selects.
        List<TInner> inners;
        if (join.Kind != DocJoinKind.Left)
            inners = await CosmosInnersAsync<TResult, TInner, TKey>(query, join, inner, join.CosmosAllText!, null, ct).ConfigureAwait(false);
        else if (keys.Count > 0)
            inners = await CosmosInnersAsync(query, join, inner, join.CosmosText + DocFamilies.AndMarker, keys, ct).ConfigureAwait(false);
        else
            inners = [];

        var build = join.Result!;
        var results = new List<TResult>();
        var skip = Math.Max(query.SkipCount, 0);
        bool Add(object? outer, object? matched)
        {
            if (skip > 0)
            {
                skip--;
                return true;
            }

            results.Add((TResult)build(outer, matched));
            return results.Count != query.TakeCount;
        }

        if (join.Kind == DocJoinKind.Right)
        {
            var byKey = new Dictionary<TKey, List<TOuter>>();
            foreach (var outer in outers)
            {
                var key = outerKey(outer);
                if (!byKey.TryGetValue(key, out var list))
                    byKey[key] = list = [];
                list.Add(outer);
            }

            foreach (var each in inners)
            {
                if (!byKey.TryGetValue(innerKey(each), out var list))
                {
                    if (!Add(null, each))
                        return results;
                    continue;
                }

                foreach (var outer in list)
                {
                    if (!Add(outer, each))
                        return results;
                }
            }

            return results;
        }

        var matches = new Dictionary<TKey, List<TInner>>();
        foreach (var each in inners)
        {
            var key = innerKey(each);
            if (!keys.Contains(key))
                continue; // a FullJoin's inner document alone: after the outer ones
            if (!matches.TryGetValue(key, out var list))
                matches[key] = list = [];
            list.Add(each);
        }

        foreach (var outer in outers)
        {
            if (!matches.TryGetValue(outerKey(outer), out var list))
            {
                if (!Add(outer, null))
                    return results;
                continue;
            }

            foreach (var each in list)
            {
                if (!Add(outer, each))
                    return results;
            }
        }

        if (join.Kind == DocJoinKind.Full)
        {
            foreach (var each in inners)
            {
                if (!keys.Contains(innerKey(each)) && !Add(null, each))
                    return results;
            }
        }

        return results;
    }
}
