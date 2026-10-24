using System.Text.Json;

namespace Pragmatic.Persistence.Documents.Execution;

/// <summary>
///     A Join of two collections. MongoDB: one <c>aggregate</c> on the outer collection, its <c>$match</c> and <c>$sort</c>, a <c>$lookup</c> of the
///     inner collection on the two keys with the inner chain's <c>$match</c> in its pipeline, <c>$unwind</c> (a document without a match drops out,
///     as in LINQ's Join), then <c>$skip</c>/<c>$limit</c> on the joined rows. Cosmos DB has no join between containers: the outer documents read as
///     the outer chain says (its partition rules), then the inner documents of their keys in a second query (a filter: served across partitions),
///     joined here in the outer order, Skip and Take on the joined rows.
/// </summary>
public static partial class DocExecution
{
    private const string Joined = "__j";

    public static async ValueTask<List<TResult>> JoinListAsync<TOuter, TInner, TKey, TResult>(DocQuery<TResult> query, DocPlan<TOuter> plan, DocJoin<TInner> join, CancellationToken ct)
        where TOuter : IDocumentEntity<TOuter>
        where TInner : IDocumentEntity<TInner>
        where TKey : notnull
    {
        using var prepared = DocFamilies.Use(await DocFamilies.PrepareAsync<TOuter>(query.Session, ct).ConfigureAwait(false)); // a trait's child: its visible parents, read first
        using var preparedInner = DocFamilies.Use(await DocFamilies.PrepareAsync<TInner>(query.Session, ct).ConfigureAwait(false)); // a trait's child: its visible parents, read first
        if (query.TakeCount == 0)
            return [];
        var inner = (DocQuery<TInner>)query.Slot(join.Slot)!;
        var outerKey = (Func<TOuter, TKey>)query.Slot(join.Slot + 1)!;
        var innerKey = (Func<TInner, TKey>)query.Slot(join.Slot + 2)!;
        var result = (Func<TOuter, TInner, TResult>)query.Slot(join.Slot + 3)!;
        if (query.Session.Cosmos is not null)
            return await CosmosJoinAsync(query, plan, join, inner, outerKey, innerKey, result, ct).ConfigureAwait(false);

        var session = query.Session;
        var slots = query.SlotArray;
        var innerSlots = inner.SlotArray;
        var reply = await session.RunAsync(w =>
        {
            w.Write("aggregate", TOuter.Collection);
            w.StartArray("pipeline");
            var stage = 0;
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
            w.StartDocument(stage++);
            w.Write("$unwind", "$" + Joined);
            w.EndDocument();
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
        var documents = await session.Cursor(reply).ToListAsync(ct).ConfigureAwait(false);
        var results = new List<TResult>(documents.Count);
        foreach (var document in documents)
        {
            document.TryGetDocument(Joined, out var matched);
            results.Add(result(TOuter.ReadDocument(document.Bytes.Span), TInner.ReadDocument(matched.Bytes.Span))); // the outer reader skips the joined field
        }

        return results;
    }

    private static async ValueTask<List<TResult>> CosmosJoinAsync<TOuter, TInner, TKey, TResult>(DocQuery<TResult> query, DocPlan<TOuter> plan, DocJoin<TInner> join, DocQuery<TInner> inner,
        Func<TOuter, TKey> outerKey, Func<TInner, TKey> innerKey, Func<TOuter, TInner, TResult> result, CancellationToken ct)
        where TOuter : IDocumentEntity<TOuter>
        where TInner : IDocumentEntity<TInner>
        where TKey : notnull
    {
        var outers = await CosmosListAsync(query.Unpaged<TOuter>(), plan, ct).ConfigureAwait(false); // the outer documents whole: the paging is the joined rows'
        if (outers.Count == 0)
            return [];
        var keys = new HashSet<TKey>();
        foreach (var outer in outers)
            keys.Add(outerKey(outer));

        var w = Json(out var buffer);
        w.WriteStartObject();
        var filters = query.Session.Filters;
        var leaves = new List<(DocCompiledPredicate Leaf, object?[] Values, string Prefix)>();
        w.WriteString("query", DocFamilies.CosmosText<TInner>(join.CosmosText + DocFamilies.AndMarker, filters, null, leaves)); // the joined side's families after its WHERE
        w.WriteStartArray("parameters");
        join.CosmosParameters?.Invoke(w, inner.SlotArray);
        DocFamilies.CosmosParameters<TInner>(w, filters, leaves);
        w.WriteStartObject();
        w.WriteString("name", "@__keys");
        w.WriteStartArray("value");
        foreach (var key in keys)
            WriteKey(w, key);
        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteEndArray();
        w.WriteEndObject();
        w.Flush();
        var body = buffer.WrittenSpan.ToArray();

        var matches = new Dictionary<TKey, List<TInner>>();
        await foreach (var document in query.Session.CosmosClient.QueryAsync(query.Session.Database, TInner.Collection, body, DocFamilies.Partition<TInner>(filters), null, ct).ConfigureAwait(false))
        {
            var each = TInner.ReadJson(document);
            var key = innerKey(each);
            if (!matches.TryGetValue(key, out var list))
                matches[key] = list = [];
            list.Add(each);
        }

        var results = new List<TResult>();
        var skip = Math.Max(query.SkipCount, 0);
        foreach (var outer in outers)
        {
            if (!matches.TryGetValue(outerKey(outer), out var list))
                continue;
            foreach (var each in list)
            {
                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                results.Add(result(outer, each));
                if (results.Count == query.TakeCount)
                    return results;
            }
        }

        return results;
    }

    /// <summary>A key as the documents' JSON holds it: a Guid as its text (the id's form), a number or a text as it is.</summary>
    private static void WriteKey<TKey>(Utf8JsonWriter w, TKey key)
    {
        switch (key)
        {
            case Guid guid:
                w.WriteStringValue(guid.ToString("D"));
                break;
            case string text:
                w.WriteStringValue(text);
                break;
            case int number:
                w.WriteNumberValue(number);
                break;
            case long number:
                w.WriteNumberValue(number);
                break;
            default:
                throw new NotSupportedException($"A join key of {typeof(TKey).Name} is not written to Cosmos DB by this version (a Guid, a text, an int or a long).");
        }
    }
}
