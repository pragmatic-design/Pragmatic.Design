using System.Text.Json;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Execution;

/// <summary>
///     The keys of a tree's descendants or ancestors along a self-referencing parent key (Pragmatic's <c>[GenerateHierarchy]</c>), as the Sql
///     engine's <c>SqlHierarchy</c> and Pragmatic's CTE walk them: bounded in depth, the starting document left out, no filter (the generated
///     methods read the documents with a chain, the caller's filters applied). MongoDB walks in one <c>$graphLookup</c>; Cosmos DB, which has no
///     recursion, one query per level (each a filter across partitions).
/// </summary>
public static class DocHierarchy
{
    /// <summary>The default bound, Pragmatic's (<c>HierarchyCteSql.Build</c>'s <c>maxDepth</c>).</summary>
    public const int MaxDepth = 100;

    /// <summary>The keys reached from <paramref name="start" />: its descendants when <paramref name="descendants" />, its ancestors otherwise.</summary>
    /// <remarks>
    ///     Unconstrained, as <c>DocSession.Query&lt;T&gt;</c>: the generator binds the call where the entity does not implement its document
    ///     contract yet; the collection is the registered tracker's (<see cref="DocTrackers" />).
    /// </remarks>
    public static async Task<List<Guid>> KeysAsync<T>(DocSession session, string parentField, Guid start, bool descendants, int maxDepth = MaxDepth, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var collection = DocTrackers.For(typeof(T)).Collection;
        return session.Cosmos is not null
            ? await CosmosKeysAsync(session, collection, parentField, start, descendants, maxDepth, ct).ConfigureAwait(false)
            : await MongoKeysAsync(session, collection, parentField, start, descendants, maxDepth, ct).ConfigureAwait(false);
    }

    private static async Task<List<Guid>> MongoKeysAsync(DocSession session, string collection, string parentField, Guid start, bool descendants, int maxDepth, CancellationToken ct)
    {
        var reply = await session.RunAsync(w =>
        {
            w.Write("aggregate", collection);
            w.StartArray("pipeline");
            w.StartDocument(0);
            w.StartDocument("$match");
            w.Write("_id", start);
            w.EndDocument();
            w.EndDocument();
            w.StartDocument(1);
            w.StartDocument("$graphLookup");
            w.Write("from", collection);
            w.Write("startWith", descendants ? "$_id" : "$" + parentField);
            w.Write("connectFromField", descendants ? "_id" : parentField);
            w.Write("connectToField", descendants ? parentField : "_id");
            w.Write("as", "h");
            w.Write("maxDepth", maxDepth - 1); // 0 is one step: the bound counts the steps, as the CTE's depth
            w.EndDocument();
            w.EndDocument();
            w.StartDocument(2);
            w.StartDocument("$project");
            w.Write("h._id", 1);
            w.EndDocument();
            w.EndDocument();
            w.EndArray();
            w.StartDocument("cursor");
            w.EndDocument();
        }, ct).ConfigureAwait(false);
        var keys = new List<Guid>();
        foreach (var document in await session.Cursor(reply).ToListAsync(ct).ConfigureAwait(false))
        {
            if (!document.TryGetDocument("h", out var reached))
                continue;
            foreach (var node in reached.Documents())
            {
                var reader = new BsonReader(node.Bytes.Span);
                while (reader.ReadElement(out _, out var name))
                {
                    if (!name.SequenceEqual("_id"u8))
                    {
                        reader.Skip();
                        continue;
                    }

                    var key = reader.ReadGuid();
                    if (key != start) // a cycle back to the start: the start is left out, as the CTE's
                        keys.Add(key);
                }
            }
        }

        return keys;
    }

    private static async Task<List<Guid>> CosmosKeysAsync(DocSession session, string collection, string parentField, Guid start, bool descendants, int maxDepth, CancellationToken ct)
    {
        var seen = new HashSet<Guid> { start };
        var keys = new List<Guid>();
        var frontier = new List<Guid> { start };
        // Descendants: the documents whose parent is in the level. Ancestors: the parents of the level's documents.
        var text = descendants
            ? $"SELECT VALUE c.id FROM c WHERE ARRAY_CONTAINS(@__level, c[\"{parentField}\"])"
            : $"SELECT VALUE c[\"{parentField}\"] FROM c WHERE ARRAY_CONTAINS(@__level, c.id) AND IS_STRING(c[\"{parentField}\"])";
        for (var depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();
                w.WriteString("query", text);
                w.WriteStartArray("parameters");
                w.WriteStartObject();
                w.WriteString("name", "@__level");
                w.WriteStartArray("value");
                foreach (var key in frontier)
                    w.WriteStringValue(key.ToString("D"));
                w.WriteEndArray();
                w.WriteEndObject();
                w.WriteEndArray();
                w.WriteEndObject();
            }

            var next = new List<Guid>();
            await foreach (var value in session.CosmosClient.QueryAsync(session.Database, collection, stream.ToArray(), null, null, ct).ConfigureAwait(false))
            {
                if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var key) && seen.Add(key))
                    next.Add(key);
            }

            keys.AddRange(next);
            frontier = next;
        }

        return keys;
    }
}
