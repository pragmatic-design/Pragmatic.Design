using System.Buffers;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Pragmatic.Persistence.Documents.Execution;

/// <summary>
///     The terminals on Cosmos DB: the plan's Cosmos query sent through the REST API. In one partition (the chain names the partition key in an
///     equality) every form runs on the server, Skip and Take as OFFSET/LIMIT. Across partitions the gateway serves filters and projections only
///     (learn.microsoft.com, "Queries that cannot be served by gateway": TOP, ORDER BY, OFFSET LIMIT, aggregates, DISTINCT, GROUP BY): an
///     unordered list or Any runs, with Skip and Take applied as the documents arrive; an ordering, a count or an aggregate is refused with the
///     remedy, not sent to fail.
/// </summary>
public static partial class DocExecution
{
    private const string CrossPartition = "Cosmos DB's gateway serves ORDER BY, OFFSET/LIMIT, COUNT and aggregates in one partition only: add the partition key's equality to the chain (Where((o, k) => o.PartitionKeyField == k, value)), or read without them";

    private static async ValueTask<List<TResult>> CosmosListAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        var cosmos = CosmosOf(plan);
        var slots = query.SlotArray;
        var partition = Partition<TEntity>(cosmos, slots, query.Session.Filters);
        if (partition is null && cosmos.Stateful)
            return await CosmosMergedAsync(query, plan, cosmos, ct).ConfigureAwait(false); // ordered across partitions: range by range, merged here
        // A point read is a GET of the document: it carries no condition, so not where a Pragmatic entity's families apply.
        if (cosmos.PointId is not null && partition is not null && query.SkipCount <= 0 && cosmos.PredicateSlot < 0 && !DocFamilies.AnyApplies<TEntity>(query.Session.Filters))
            return await CosmosPointReadAsync(query, plan, cosmos.PointId(slots), partition, ct).ConfigureAwait(false);
        var paged = partition is not null && (query.SkipCount >= 0 || query.TakeCount >= 0);
        var text = paged ? cosmos.PagedText : cosmos.Text;
        var body = Body<TEntity>(text, cosmos, slots, query.Session.Filters, paged ? Math.Max(query.SkipCount, 0) : -1, paged ? (query.TakeCount >= 0 ? query.TakeCount : int.MaxValue) : -1);
        var select = plan.SelectSlot >= 0 ? (Func<TEntity, TResult>)query.Slot(plan.SelectSlot)! : null;
        var results = new List<TResult>();
        var skip = paged ? 0 : Math.Max(query.SkipCount, 0); // across partitions: unordered, skipped and taken as the documents arrive
        var take = paged || query.TakeCount < 0 ? int.MaxValue : query.TakeCount;
        await foreach (var document in query.Session.CosmosClient.QueryAsync(query.Session.Database, TEntity.Collection, body, partition, null, ct).ConfigureAwait(false))
        {
            if (skip > 0)
            {
                skip--;
                continue;
            }

            var entity = TEntity.ReadJson(document);
            query.Session.Remember(entity, document);
            results.Add(select is null ? (TResult)(object)entity : select(entity));
            if (results.Count == take)
                break;
        }

        return results;
    }

    /// <summary>One document by its id and partition key: a GET of the document (a point read), no query; none when it is not there.</summary>
    private static async ValueTask<List<TResult>> CosmosPointReadAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, string id, string partition, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        var session = query.Session;
        var link = $"dbs/{session.Database}/colls/{TEntity.Collection}/docs/{Uri.EscapeDataString(id)}";
        var response = await session.CosmosClient.SendAsync(HttpMethod.Get, "docs", link, link, null, h => h.TryAddWithoutValidation("x-ms-documentdb-partitionkey", partition), ct: ct, accepted: HttpStatusCode.NotFound).ConfigureAwait(false);
        if (response.Status == HttpStatusCode.NotFound)
            return [];
        using var document = JsonDocument.Parse(response.Body);
        var entity = TEntity.ReadJson(document.RootElement);
        session.Remember(entity, document.RootElement);
        var select = plan.SelectSlot >= 0 ? (Func<TEntity, TResult>)query.Slot(plan.SelectSlot)! : null;
        return [select is null ? (TResult)(object)entity : select(entity)];
    }

    private static async ValueTask<long> CosmosCountAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        var cosmos = CosmosOf(plan);
        var slots = query.SlotArray;
        if (Partition<TEntity>(cosmos, slots, query.Session.Filters) is null && query.SkipCount < 0 && query.TakeCount < 0)
            return await CosmosCountAcrossAsync<TEntity, TResult>(query, cosmos, slots, ct).ConfigureAwait(false);
        var value = await CosmosScalarAsync(query, plan, ct).ConfigureAwait(false);
        return value is { ValueKind: JsonValueKind.Number } number ? number.GetInt64() : 0;
    }

    /// <summary>
    ///     A COUNT across partitions (owner's decision of 2026-10-22): the gateway serves it in one partition key range only, so it is sent to each
    ///     range of the container (<c>x-ms-documentdb-partitionkeyrangeid</c>) and the counts added, as the SDKs do.
    /// </summary>
    private static async ValueTask<long> CosmosCountAcrossAsync<TEntity, TResult>(DocQuery<TResult> query, DocCosmosQuery cosmos, object?[] slots, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        var session = query.Session;
        var body = Body<TEntity>(cosmos.Text, cosmos, slots, session.Filters, -1, -1);
        long total = 0;
        foreach (var range in await session.CosmosClient.PartitionKeyRangesAsync(session.Database, TEntity.Collection, ct).ConfigureAwait(false))
        {
            await foreach (var value in session.CosmosClient.QueryAsync(session.Database, TEntity.Collection, body, null, null, range, ct).ConfigureAwait(false))
            {
                if (value.ValueKind == JsonValueKind.Number)
                    total += value.GetInt64();
            }
        }

        return total;
    }

    private static async ValueTask<bool> CosmosAnyAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        var cosmos = CosmosOf(plan);
        if (query.SkipCount > 0)
            throw new NotSupportedException($"{plan.Origin}: Skip before Any is not translated for Cosmos DB.");
        var slots = query.SlotArray;
        await foreach (var _ in query.Session.CosmosClient.QueryAsync(query.Session.Database, TEntity.Collection, Body<TEntity>(cosmos.Text, cosmos, slots, query.Session.Filters, -1, -1), Partition<TEntity>(cosmos, slots, query.Session.Filters), 1, ct).ConfigureAwait(false))
            return true;
        return false;
    }

    private static async ValueTask<TValue> CosmosAggregateAsync<TEntity, TResult, TValue>(DocQuery<TResult> query, DocPlan<TEntity> plan, string accumulator, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        var value = await CosmosScalarAsync(query, plan, ct).ConfigureAwait(false);
        if (value is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } found)
        {
            if (accumulator == "$sum" || default(TValue) is null)
                return default!;
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        object result = typeof(TValue) == typeof(decimal) || typeof(TValue) == typeof(decimal?) ? found.GetDecimal()
            : typeof(TValue) == typeof(int) || typeof(TValue) == typeof(int?) ? found.GetInt32()
            : typeof(TValue) == typeof(long) || typeof(TValue) == typeof(long?) ? found.GetInt64()
            : typeof(TValue) == typeof(double) || typeof(TValue) == typeof(double?) ? found.GetDouble()
            : typeof(TValue) == typeof(DateTimeOffset) || typeof(TValue) == typeof(DateTimeOffset?) ? DocJson.ReadInstant(found)
            : typeof(TValue) == typeof(string) ? found.GetString()!
            : throw new NotSupportedException($"An aggregate of {typeof(TValue).Name} is not read back by this version.");
        return (TValue)result;
    }

    /// <summary>A COUNT or an aggregate: one value, in one partition.</summary>
    private static async ValueTask<JsonElement?> CosmosScalarAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        var cosmos = CosmosOf(plan);
        var slots = query.SlotArray;
        var partition = Partition<TEntity>(cosmos, slots, query.Session.Filters) ?? throw new NotSupportedException($"{plan.Origin}: {CrossPartition}.");
        if (query.SkipCount >= 0 || query.TakeCount >= 0)
            throw new NotSupportedException($"{plan.Origin}: Skip or Take before a count or an aggregate is not translated for Cosmos DB.");
        await foreach (var value in query.Session.CosmosClient.QueryAsync(query.Session.Database, TEntity.Collection, Body<TEntity>(cosmos.Text, cosmos, slots, query.Session.Filters, -1, -1), partition, null, ct).ConfigureAwait(false))
            return value;
        return null;
    }

    private static DocCosmosQuery CosmosOf<TEntity>(DocPlan<TEntity> plan) where TEntity : IDocumentEntity<TEntity>
        => plan.Cosmos ?? throw new NotSupportedException($"{plan.Origin}: the chain has no Cosmos DB query (see the generator's note at the call site).");

    /// <summary>
    ///     The <c>x-ms-documentdb-partitionkey</c> header from the chain's partition key value, or from an active tenant family of an entity
    ///     partitioned by its tenant (a Pragmatic tenant's entity: the caller's reads stay in one partition); null when neither names one.
    /// </summary>
    private static string? Partition<TEntity>(DocCosmosQuery cosmos, object?[] slots, IDocFilters? filters) where TEntity : IDocumentEntity<TEntity>
    {
        if (cosmos.Partition is null)
            return DocFamilies.Partition<TEntity>(filters);
        var w = Json(out var buffer);
        w.WriteStartArray();
        cosmos.Partition(w, slots);
        w.WriteEndArray();
        w.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The query's body: the text (a Pragmatic entity's families in it) and its parameters (the chain's, the families', then the page's when there is one).</summary>
    private static byte[] Body<TEntity>(string text, DocCosmosQuery cosmos, object?[] slots, IDocFilters? filters, int skip, int take) where TEntity : IDocumentEntity<TEntity>
    {
        var w = Json(out var buffer);
        var predicate = cosmos.PredicateSlot >= 0 ? (DocPredicate<TEntity>?)slots[cosmos.PredicateSlot] : null;
        var leaves = new List<(DocCompiledPredicate Leaf, object?[] Values, string Prefix)>();
        if (cosmos.OrderingSlot >= 0)
            text = text.Replace("{order}", ((DocOrdering<TEntity>)slots[cosmos.OrderingSlot]!).CosmosText(), StringComparison.Ordinal);
        w.WriteStartObject();
        w.WriteString("query", DocFamilies.CosmosText(text, filters, predicate, leaves));
        w.WriteStartArray("parameters");
        cosmos.Parameters?.Invoke(w, slots);
        DocFamilies.CosmosParameters<TEntity>(w, filters, leaves);
        if (skip >= 0)
        {
            Parameter(w, "@__skip", skip);
            Parameter(w, "@__take", take);
        }

        w.WriteEndArray();
        w.WriteEndObject();
        w.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    [ThreadStatic]
    private static ArrayBufferWriter<byte>? _buffer;

    [ThreadStatic]
    private static Utf8JsonWriter? _writer;

    /// <summary>The thread's JSON writer over its buffer, both emptied: a body or a header is written and copied out before the next call, with no await between.</summary>
    private static Utf8JsonWriter Json(out ArrayBufferWriter<byte> buffer)
    {
        buffer = _buffer ??= new ArrayBufferWriter<byte>(256);
        buffer.ResetWrittenCount();
        if (_writer is null)
            _writer = new Utf8JsonWriter(buffer);
        else
            _writer.Reset(buffer);
        return _writer;
    }

    private static void Parameter(Utf8JsonWriter w, string name, int value)
    {
        w.WriteStartObject();
        w.WriteString("name", name);
        w.WriteNumber("value", value);
        w.WriteEndObject();
    }
}
