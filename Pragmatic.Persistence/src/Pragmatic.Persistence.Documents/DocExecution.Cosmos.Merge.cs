using System.Text.Json;

namespace Pragmatic.Persistence.Documents;

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

/// <summary>
///     Cosmos DB's order of values, which a merge across partitions must follow, each key ascending or descending. ASSUMPTION, not checked
///     against the service's documentation in this step: undefined, then null, false, true, numbers, strings, by type first; strings by
///     ordinal (UTF-16) comparison, which differs from a byte order only past the BMP. The emulator's single range cannot tell (INVESTIGATE).
/// </summary>
public sealed class CosmosOrder(IReadOnlyList<(IReadOnlyList<string> Segments, bool Descending)> keys) : IComparer<JsonElement>
{
    public int Compare(JsonElement x, JsonElement y)
    {
        foreach (var (segments, descending) in keys)
        {
            var compared = CompareValues(At(x, segments), At(y, segments));
            if (compared != 0)
                return descending ? -compared : compared;
        }

        return 0;
    }

    /// <summary>The value at a path; null (undefined) when a segment is missing.</summary>
    private static JsonElement? At(JsonElement document, IReadOnlyList<string> segments)
    {
        var current = document;
        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return null;
        }

        return current;
    }

    internal static int CompareValues(JsonElement? x, JsonElement? y)
    {
        var (rx, ry) = (Rank(x), Rank(y));
        if (rx != ry)
            return rx.CompareTo(ry);
        return rx switch
        {
            2 => x!.Value.GetBoolean().CompareTo(y!.Value.GetBoolean()),
            3 => x!.Value.TryGetDecimal(out var a) && y!.Value.TryGetDecimal(out var b) ? a.CompareTo(b) : x.Value.GetDouble().CompareTo(y!.Value.GetDouble()),
            4 => string.CompareOrdinal(x!.Value.GetString(), y!.Value.GetString()),
            _ => 0, // undefined with undefined, null with null; an array or an object is not an ordering key
        };
    }

    private static int Rank(JsonElement? value) => value?.ValueKind switch
    {
        null or JsonValueKind.Undefined => 0,
        JsonValueKind.Null => 1,
        JsonValueKind.False or JsonValueKind.True => 2,
        JsonValueKind.Number => 3,
        JsonValueKind.String => 4,
        _ => 5,
    };
}
