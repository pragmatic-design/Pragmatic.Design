using System.Text.Json;

namespace Pragmatic.Persistence.Documents.Execution;

/// <summary>
///     An ordered read across Cosmos DB's partitions (owner's decision of 2026-10-22). The gateway serves ORDER BY in one partition key range
///     only, so the query goes to each range with its ORDER BY and, when the chain takes a page, <c>OFFSET 0 LIMIT skip + take</c>; the ranges'
///     documents are merged here in Cosmos DB's order, then skipped and taken, as the SDKs do. Every range reads up to skip + take documents.
/// </summary>
public static partial class DocExecution
{
    private static async ValueTask<List<TResult>> CosmosMergedAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, DocCosmosQuery cosmos, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        var session = query.Session;
        var slots = query.SlotArray;
        var keys = OrderKeys<TEntity>(cosmos, slots) ?? throw new NotSupportedException($"{plan.Origin}: {CrossPartition}.");
        var skip = Math.Max(query.SkipCount, 0);
        var limit = query.TakeCount >= 0 ? skip + query.TakeCount : -1;
        var body = limit >= 0
            ? Body<TEntity>(cosmos.PagedText, cosmos, slots, session.Filters, 0, limit)
            : Body<TEntity>(cosmos.Text, cosmos, slots, session.Filters, -1, -1);
        var documents = new List<JsonElement>();
        foreach (var range in await session.CosmosClient.PartitionKeyRangesAsync(session.Database, TEntity.Collection, ct).ConfigureAwait(false))
        {
            await foreach (var document in session.CosmosClient.QueryAsync(session.Database, TEntity.Collection, body, null, null, range, ct).ConfigureAwait(false))
                documents.Add(document);
        }

        var order = new CosmosOrder(keys);
        var page = documents.OrderBy(d => d, order).Skip(skip); // OrderBy is stable: equal keys keep the ranges' order
        if (query.TakeCount >= 0)
            page = page.Take(query.TakeCount);
        var select = plan.SelectSlot >= 0 ? (Func<TEntity, TResult>)query.Slot(plan.SelectSlot)! : null;
        var results = new List<TResult>();
        foreach (var document in page)
        {
            var entity = TEntity.ReadJson(document);
            session.Remember(entity, document);
            results.Add(select is null ? (TResult)(object)entity : select(entity));
        }

        return results;
    }

    /// <summary>The keys the read is ordered by, as the documents hold them: the runtime ordering's, or the chain's OrderBy; null when it has none.</summary>
    private static IReadOnlyList<(IReadOnlyList<string> Segments, bool Descending)>? OrderKeys<TEntity>(DocCosmosQuery cosmos, object?[] slots)
    {
        if (cosmos.OrderingSlot >= 0)
            return ((DocOrdering<TEntity>)slots[cosmos.OrderingSlot]!).Keys.Select(k => (k.Key.Segments, k.Descending)).ToList();
        return cosmos.SortKeys?.Select(k => ((IReadOnlyList<string>)k.Segments, k.Descending)).ToList();
    }
}
