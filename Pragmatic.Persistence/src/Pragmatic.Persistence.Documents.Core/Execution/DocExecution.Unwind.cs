namespace Pragmatic.Persistence.Documents.Execution;

/// <summary>
///     A SelectMany over an embedded list (<c>SelectMany(o => o.Lines)</c>): on MongoDB an <c>aggregate</c> of the filter's <c>$match</c>, the
///     documents' <c>$sort</c>, <c>$unwind</c> and <c>$replaceRoot</c> of the list, then Skip and Take as <c>$skip</c>/<c>$limit</c>; on Cosmos DB the
///     query <c>SELECT VALUE u FROM c JOIN u IN c.List</c> (paged in the partition, as the documents' lists are). Each element read by its type's
///     generated reader.
/// </summary>
public static partial class DocExecution
{
    public static async ValueTask<List<TElement>> UnwindListAsync<TEntity, TElement>(DocQuery<TElement> query, DocPlan<TEntity> plan, CancellationToken ct)
        where TEntity : IDocumentEntity<TEntity>
        where TElement : IEmbeddedDocument<TElement>
    {
        using var prepared = DocFamilies.Use(await DocFamilies.PrepareAsync<TEntity>(query.Session, ct).ConfigureAwait(false)); // a trait's child: its visible parents, read first
        if (query.TakeCount == 0)
            return []; // MongoDB's $limit takes a positive number
        if (query.Session.Cosmos is not null)
            return await CosmosUnwindAsync(query, plan, ct).ConfigureAwait(false);
        var session = query.Session;
        var slots = query.SlotArray;
        var reply = await session.RunAsync(w =>
        {
            w.Write("aggregate", TEntity.Collection);
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
            w.Write("$unwind", "$" + plan.Unwind);
            w.EndDocument();
            w.StartDocument(stage++);
            w.StartDocument("$replaceRoot");
            w.Write("newRoot", "$" + plan.Unwind);
            w.EndDocument();
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
        var results = new List<TElement>(documents.Count);
        foreach (var document in documents)
            results.Add(TElement.ReadDocument(document.Bytes.Span));
        return results;
    }

    private static async ValueTask<List<TElement>> CosmosUnwindAsync<TEntity, TElement>(DocQuery<TElement> query, DocPlan<TEntity> plan, CancellationToken ct)
        where TEntity : IDocumentEntity<TEntity>
        where TElement : IEmbeddedDocument<TElement>
    {
        var cosmos = CosmosOf(plan);
        var slots = query.SlotArray;
        var partition = Partition<TEntity>(cosmos, slots, query.Session.Filters);
        if (partition is null && cosmos.Stateful)
            throw new NotSupportedException($"{plan.Origin}: {CrossPartition}.");
        var paged = partition is not null && (query.SkipCount >= 0 || query.TakeCount >= 0);
        var body = Body<TEntity>(paged ? cosmos.PagedText : cosmos.Text, cosmos, slots, query.Session.Filters, paged ? Math.Max(query.SkipCount, 0) : -1, paged ? (query.TakeCount >= 0 ? query.TakeCount : int.MaxValue) : -1);
        var results = new List<TElement>();
        var skip = paged ? 0 : Math.Max(query.SkipCount, 0); // across partitions: skipped and taken as the elements arrive
        var take = paged || query.TakeCount < 0 ? int.MaxValue : query.TakeCount;
        await foreach (var element in query.Session.CosmosClient.QueryAsync(query.Session.Database, TEntity.Collection, body, partition, null, ct).ConfigureAwait(false))
        {
            if (skip > 0)
            {
                skip--;
                continue;
            }

            results.Add(TElement.ReadJson(element));
            if (results.Count == take)
                break;
        }

        return results;
    }
}
