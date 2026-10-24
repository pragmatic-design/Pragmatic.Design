using System.Net;
using System.Text.Json;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     Cosmos DB: each container created with its partition key, its unique keys and its composite indexes, or compared with the one there.
///     The composite indexes are the indexing policy's and change in place (<c>PUT</c>); the partition key and the unique keys are fixed at
///     creation, so a difference in them is reported, not applied. Every other path is indexed by the container's automatic policy.
/// </summary>
public static partial class DocSchema
{
    private static async Task<DocSchemaReport> EnsureCosmosAsync(DocSession session, IReadOnlyList<DocCollectionSchema> collections, CancellationToken ct)
    {
        var client = session.CosmosClient;
        var changes = new List<DocSchemaChange>();
        await client.CreateDatabaseIfNotExistsAsync(session.Database, ct).ConfigureAwait(false);
        foreach (var collection in collections)
        {
            var uniqueKeys = new List<string[]>();
            foreach (var index in collection.Indexes.Where(i => i.Unique))
            {
                if (UniqueKeyOf(index, collection.PartitionKeyPath, out var why) is { } paths)
                    uniqueKeys.Add(paths);
                else
                    changes.Add(new DocSchemaChange(collection.Collection, index.Name, DocSchemaOutcome.NotApplied, why));
            }

            var link = $"dbs/{session.Database}/colls/{collection.Collection}";
            var found = await client.SendAsync(HttpMethod.Get, "colls", link, link, null, null, ct: ct, accepted: HttpStatusCode.NotFound).ConfigureAwait(false);
            if (found.Status == HttpStatusCode.NotFound)
            {
                var body = ContainerBody(collection.Collection, w => WritePartitionKey(w, collection.PartitionKeyPath), w => WriteUniqueKeys(w, uniqueKeys), null, collection.Composites);
                await client.SendAsync(HttpMethod.Post, "colls", $"dbs/{session.Database}", $"dbs/{session.Database}/colls", body, null, ct: ct).ConfigureAwait(false);
                changes.Add(new DocSchemaChange(collection.Collection, "container", DocSchemaOutcome.Created));
                continue;
            }

            using var container = JsonDocument.Parse(found.Body);
            var root = container.RootElement;
            var partition = root.GetProperty("partitionKey").GetProperty("paths")[0].GetString();
            changes.Add(partition == collection.PartitionKeyPath
                ? new DocSchemaChange(collection.Collection, "partition key", DocSchemaOutcome.Unchanged)
                : new DocSchemaChange(collection.Collection, "partition key", DocSchemaOutcome.NotApplied,
                    $"the container is partitioned by {partition}, the schema declares {collection.PartitionKeyPath}: a partition key is fixed at creation (recreate the container and move its documents)"));

            var existingKeys = root.TryGetProperty("uniqueKeyPolicy", out var policy) && policy.TryGetProperty("uniqueKeys", out var keys)
                ? keys.EnumerateArray().Select(k => k.GetProperty("paths").EnumerateArray().Select(p => p.GetString()!).ToArray()).ToList()
                : [];
            changes.Add(SameSets(existingKeys, uniqueKeys)
                ? new DocSchemaChange(collection.Collection, "unique keys", DocSchemaOutcome.Unchanged)
                : new DocSchemaChange(collection.Collection, "unique keys", DocSchemaOutcome.NotApplied,
                    $"the container holds the unique keys [{Describe(existingKeys)}], the schema declares [{Describe(uniqueKeys)}]: Cosmos DB fixes them at creation (recreate the container and move its documents)"));

            var indexing = root.TryGetProperty("indexingPolicy", out var p) ? p : default;
            var existingComposites = indexing.ValueKind == JsonValueKind.Object && indexing.TryGetProperty("compositeIndexes", out var composites)
                ? composites.EnumerateArray().Select(c => c.EnumerateArray().Select(e => (e.GetProperty("path").GetString()!, e.TryGetProperty("order", out var o) && o.GetString() == "descending")).ToArray()).ToList()
                : [];
            if (SameSets(existingComposites, collection.Composites.ToList()))
            {
                changes.Add(new DocSchemaChange(collection.Collection, "composite indexes", DocSchemaOutcome.Unchanged));
                continue;
            }

            // The indexing policy changes in place; the partition key and the unique keys are sent back as the container has them.
            var replaced = ContainerBody(collection.Collection, w => root.GetProperty("partitionKey").WriteTo(w),
                w => { if (root.TryGetProperty("uniqueKeyPolicy", out var kept)) kept.WriteTo(w); else WriteUniqueKeys(w, []); },
                indexing.ValueKind == JsonValueKind.Object ? indexing : null, collection.Composites);
            await client.SendAsync(HttpMethod.Put, "colls", link, link, replaced, null, ct: ct).ConfigureAwait(false);
            changes.Add(new DocSchemaChange(collection.Collection, "composite indexes", DocSchemaOutcome.Updated));
        }

        return new DocSchemaReport(changes);
    }

    /// <summary>
    ///     A unique index as a Cosmos DB unique key: its paths without the partition key's field, since the logical partition is the key's scope.
    ///     Null, with the reason, when Cosmos DB cannot hold it as declared.
    /// </summary>
    private static string[]? UniqueKeyOf(DocIndex index, string partitionKeyPath, out string why)
    {
        why = "";
        if (index.LiveField is not null)
        {
            why = $"a unique key cannot be limited to the live documents ({index.LiveField} false): a deleted document would keep its value taken";
            return null;
        }

        var partitionField = partitionKeyPath.TrimStart('/');
        if (partitionField == "id")
        {
            why = "the container is partitioned by /id: a unique key holds within a logical partition, which is one document here";
            return null;
        }

        if (!index.Fields.Contains(partitionField, StringComparer.Ordinal))
        {
            why = $"the index is unique across {partitionField}s: a unique key holds within a logical partition ({partitionKeyPath}) only";
            return null;
        }

        return [.. index.Fields.Where(f => f != partitionField).Select(f => "/" + (f == "_id" ? "id" : f))];
    }

    private static byte[] ContainerBody(string id, Action<Utf8JsonWriter> partitionKey, Action<Utf8JsonWriter> uniqueKeys, JsonElement? indexing, IReadOnlyList<(string Path, bool Descending)[]> composites)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("id", id);
            w.WritePropertyName("partitionKey");
            partitionKey(w);
            w.WritePropertyName("uniqueKeyPolicy");
            uniqueKeys(w);
            w.WriteStartObject("indexingPolicy");
            w.WriteString("indexingMode", "consistent");
            w.WriteBoolean("automatic", true);
            if (indexing is { } kept && kept.TryGetProperty("includedPaths", out var included))
            {
                w.WritePropertyName("includedPaths");
                included.WriteTo(w);
            }
            else
            {
                w.WriteStartArray("includedPaths");
                w.WriteStartObject();
                w.WriteString("path", "/*");
                w.WriteEndObject();
                w.WriteEndArray();
            }

            if (indexing is { } excludedFrom && excludedFrom.TryGetProperty("excludedPaths", out var excluded))
            {
                w.WritePropertyName("excludedPaths");
                excluded.WriteTo(w);
            }

            w.WriteStartArray("compositeIndexes");
            foreach (var composite in composites)
            {
                w.WriteStartArray();
                foreach (var (path, descending) in composite)
                {
                    w.WriteStartObject();
                    w.WriteString("path", path);
                    w.WriteString("order", descending ? "descending" : "ascending");
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WritePartitionKey(Utf8JsonWriter w, string path)
    {
        w.WriteStartObject();
        w.WriteStartArray("paths");
        w.WriteStringValue(path);
        w.WriteEndArray();
        w.WriteString("kind", "Hash");
        w.WriteNumber("version", 2);
        w.WriteEndObject();
    }

    private static void WriteUniqueKeys(Utf8JsonWriter w, List<string[]> keys)
    {
        w.WriteStartObject();
        w.WriteStartArray("uniqueKeys");
        foreach (var key in keys)
        {
            w.WriteStartObject();
            w.WriteStartArray("paths");
            foreach (var path in key)
                w.WriteStringValue(path);
            w.WriteEndArray();
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>The same elements, each a sequence compared in order, whatever order the lists hold them in.</summary>
    private static bool SameSets<T>(List<T[]> left, List<T[]> right)
        => left.Count == right.Count && left.All(l => right.Any(r => r.SequenceEqual(l)));

    private static string Describe(List<string[]> keys) => string.Join("; ", keys.Select(k => string.Join(", ", k)));
}
