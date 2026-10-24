using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The terminals of a translated chain on MongoDB (called by the generated interceptors): <c>find</c> for the documents and the first one,
///     <c>count</c>, a <c>find</c> of one key for Any, an <c>aggregate</c> (<c>$match</c>, the ordering and paging, <c>$group</c>) for Max, Min
///     and Sum. Each document is read by the type's generated reader, then projected by the chain's Select when there is one.
/// </summary>
public static partial class DocExecution
{
    public static async ValueTask<List<TResult>> ToListAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        using var prepared = DocFamilies.Use(await DocFamilies.PrepareAsync<TEntity>(query.Session, ct).ConfigureAwait(false)); // a trait's child: its visible parents, read first
        if (query.TakeCount == 0)
            return []; // Take(0) is no document; MongoDB's limit 0 would be no limit
        if (query.Session.Cosmos is not null)
            return await CosmosListAsync(query, plan, ct).ConfigureAwait(false);
        var session = query.Session;
        var slots = query.SlotArray;
        var reply = await session.RunAsync(w => Find(w, plan, slots, query.SkipCount, query.TakeCount, session), ct).ConfigureAwait(false);
        var select = plan.SelectSlot >= 0 ? (Func<TEntity, TResult>)query.Slot(plan.SelectSlot)! : null;
        var results = query.TakeCount >= 0 ? new List<TResult>(Math.Min(query.TakeCount, 1024)) : [];
        if (reply.TryGetDocument("cursor", out var first) && first.TryGetInt64("id", out var id) && id == 0 && first.TryGetDocument("firstBatch", out var batch))
        {
            // The whole answer in the first batch (a page, a key): read in place, no cursor to walk or close.
            foreach (var document in batch.Documents())
            {
                var entity = TEntity.ReadDocument(document.Bytes.Span);
                results.Add(select is null ? (TResult)(object)entity : select(entity));
            }

            return results;
        }

        var cursor = session.Cursor(reply);
        await using (((IAsyncDisposable)cursor).ConfigureAwait(false)) // a cursor is enumerable too: the disposal's ConfigureAwait, named
        {
            await foreach (var document in cursor.WithCancellation(ct).ConfigureAwait(false))
            {
                var entity = TEntity.ReadDocument(document.Bytes.Span);
                results.Add(select is null ? (TResult)(object)entity : select(entity));
            }
        }

        return results;
    }

    public static async ValueTask<TResult?> FirstOrDefaultAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        var first = await ToListAsync(query.Take(1), plan, ct).ConfigureAwait(false);
        return first.Count == 0 ? default : first[0];
    }

    public static async ValueTask<long> CountAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        using var prepared = DocFamilies.Use(await DocFamilies.PrepareAsync<TEntity>(query.Session, ct).ConfigureAwait(false)); // a trait's child: its visible parents, read first
        if (query.TakeCount == 0)
            return 0; // count's limit 0 is no limit
        if (query.Session.Cosmos is not null)
            return await CosmosCountAsync(query, plan, ct).ConfigureAwait(false);
        var session = query.Session;
        var slots = query.SlotArray;
        if (session.Transaction is not null)
            return await CountInTransactionAsync(query, plan, slots, ct).ConfigureAwait(false);
        var reply = await session.RunAsync(w =>
        {
            w.Write("count", TEntity.Collection);
            plan.WriteFilter(w, "query", slots, session);
            if (query.SkipCount >= 0)
                w.Write("skip", (long)query.SkipCount);
            if (query.TakeCount >= 0)
                w.Write("limit", (long)query.TakeCount);
        }, ct).ConfigureAwait(false);
        return reply.TryGetNumber("n", out var n) ? (long)n : throw new Mongo.MongoException("The count reply holds no n.");
    }

    public static async ValueTask<bool> AnyAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        using var prepared = DocFamilies.Use(await DocFamilies.PrepareAsync<TEntity>(query.Session, ct).ConfigureAwait(false)); // a trait's child: its visible parents, read first
        if (query.TakeCount == 0)
            return false;
        if (query.Session.Cosmos is not null)
            return await CosmosAnyAsync(query, plan, ct).ConfigureAwait(false);
        var session = query.Session;
        var slots = query.SlotArray;
        var reply = await session.RunAsync(w =>
        {
            w.Write("find", TEntity.Collection);
            plan.WriteFilter(w, "filter", slots, session);
            w.StartDocument("projection");
            w.Write("_id", 1);
            w.EndDocument();
            if (query.SkipCount >= 0)
                w.Write("skip", (long)query.SkipCount);
            w.Write("limit", 1L);
            w.Write("singleBatch", true);
        }, ct).ConfigureAwait(false);
        return reply.TryGetDocument("cursor", out var cursor) && cursor.TryGetDocument("firstBatch", out var batch) && batch.Documents().Count > 0;
    }

    /// <summary>Max, Min or Sum (<paramref name="accumulator" />: <c>$max</c>, <c>$min</c>, <c>$sum</c>) of the plan's aggregate field.</summary>
    public static async ValueTask<TValue> AggregateAsync<TEntity, TResult, TValue>(DocQuery<TResult> query, DocPlan<TEntity> plan, string accumulator, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        using var prepared = DocFamilies.Use(await DocFamilies.PrepareAsync<TEntity>(query.Session, ct).ConfigureAwait(false)); // a trait's child: its visible parents, read first
        if (query.TakeCount == 0)
            return accumulator == "$sum" || default(TValue) is null ? default! : throw new InvalidOperationException("Sequence contains no elements."); // $limit 0 is refused by the server
        if (query.Session.Cosmos is not null)
            return await CosmosAggregateAsync<TEntity, TResult, TValue>(query, plan, accumulator, ct).ConfigureAwait(false);
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
            if (plan.Sorts && (query.SkipCount >= 0 || query.TakeCount >= 0))
            {
                w.StartDocument(stage++);
                w.StartDocument("$sort");
                plan.WriteSort(w, slots);
                w.EndDocument();
                w.EndDocument();
            }

            if (query.SkipCount >= 0)
            {
                w.StartDocument(stage++);
                w.Write("$skip", (long)query.SkipCount);
                w.EndDocument();
            }

            if (query.TakeCount >= 0)
            {
                w.StartDocument(stage++);
                w.Write("$limit", (long)query.TakeCount);
                w.EndDocument();
            }

            w.StartDocument(stage);
            w.StartDocument("$group");
            w.WriteNull("_id");
            w.StartDocument("v");
            w.Write(accumulator, "$" + plan.AggregateField);
            w.EndDocument();
            w.EndDocument();
            w.EndDocument();
            w.EndArray();
            w.StartDocument("cursor");
            w.EndDocument();
        }, ct).ConfigureAwait(false);
        var rows = await session.Cursor(reply).ToListAsync(ct).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            if (accumulator == "$sum")
                return default!;
            if (default(TValue) is null)
                return default!; // a nullable selector: null over no documents, as LINQ
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        return DocValues.ReadScalar<TValue>(rows[0], "v");
    }

    private static void Find<TEntity>(BsonWriter w, DocPlan<TEntity> plan, ReadOnlySpan<object?> slots, int skip, int take, DocSession session) where TEntity : IDocumentEntity<TEntity>
    {
        w.Write("find", TEntity.Collection);
        plan.WriteFilter(w, "filter", slots, session);
        if (plan.Sorts)
        {
            w.StartDocument("sort");
            plan.WriteSort(w, slots);
            w.EndDocument();
        }

        plan.WriteProjection(w);
        if (skip >= 0)
            w.Write("skip", (long)skip);
        if (take >= 0)
        {
            w.Write("limit", (long)take);
            if (take > 0)
                w.Write("batchSize", take); // the whole page in the first batch: no getMore for a page
        }
    }
}
