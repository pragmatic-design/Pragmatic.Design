using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Schema;

/// <summary>MongoDB: the declared indexes, compared with <c>listIndexes</c> by name, created or, when they differ, dropped and created again.</summary>
public static partial class DocSchema
{
    /// <summary>NamespaceNotFound: <c>listIndexes</c> on a collection that does not exist yet.</summary>
    private const int NamespaceNotFound = 26;

    private static async Task<DocSchemaReport> EnsureMongoAsync(DocSession session, IReadOnlyList<DocCollectionSchema> collections, CancellationToken ct)
    {
        var client = session.MongoClient;
        var changes = new List<DocSchemaChange>();
        foreach (var collection in collections)
        {
            var existing = await ExistingIndexesAsync(client, session.Database, collection.Collection, ct).ConfigureAwait(false);
            foreach (var index in collection.Indexes)
            {
                var outcome = DocSchemaOutcome.Created;
                if (existing.TryGetValue(index.Name, out var found))
                {
                    if (found.Matches(index))
                    {
                        changes.Add(new DocSchemaChange(collection.Collection, index.Name, DocSchemaOutcome.Unchanged));
                        continue;
                    }

                    // An index's keys and options do not change in place: it is dropped and built again as declared.
                    await client.RunCommandAsync(session.Database, w =>
                    {
                        w.Write("dropIndexes", collection.Collection);
                        w.Write("index", index.Name);
                    }, ct).ConfigureAwait(false);
                    outcome = DocSchemaOutcome.Updated;
                }

                await client.RunCommandAsync(session.Database, w =>
                {
                    w.Write("createIndexes", collection.Collection);
                    w.StartArray("indexes");
                    w.StartDocument(0);
                    w.StartDocument("key");
                    foreach (var field in index.Fields)
                        w.Write(field, 1);
                    w.EndDocument();
                    w.Write("name", index.Name);
                    if (index.Unique)
                        w.Write("unique", true);
                    if (index.LiveField is not null || index.NullField is not null)
                    {
                        w.StartDocument("partialFilterExpression");
                        if (index.LiveField is { } live)
                        {
                            w.StartDocument(live);
                            w.Write("$eq", false);
                            w.EndDocument();
                        }

                        // A null stored as null (the engine writes it, never leaves the field out): $type, which a partial filter takes on
                        // every supported server, where an equality with null is refused there.
                        if (index.NullField is { } open)
                        {
                            w.StartDocument(open);
                            w.Write("$type", "null");
                            w.EndDocument();
                        }

                        w.EndDocument();
                    }

                    w.EndDocument();
                    w.EndArray();
                }, ct).ConfigureAwait(false);
                changes.Add(new DocSchemaChange(collection.Collection, index.Name, outcome));
            }

            if (collection.Ranges is { Count: > 0 } ranges)
                changes.AddRange(await EnsureMongoRangesAsync(client, session.Database, collection.Collection, ranges, ct).ConfigureAwait(false));
        }

        return new DocSchemaReport(changes);
    }

    /// <summary>An index as <c>listIndexes</c> reports it: its key's fields in order, unique, the live and the null field of its partial filter.</summary>
    private sealed record MongoIndex(List<string> Fields, bool Unique, string? LiveField, string? NullField)
    {
        public bool Matches(DocIndex declared)
            => Fields.SequenceEqual(declared.Fields, StringComparer.Ordinal) && Unique == declared.Unique && LiveField == declared.LiveField
               && NullField == declared.NullField;
    }

    private static async Task<Dictionary<string, MongoIndex>> ExistingIndexesAsync(MongoClient client, string database, string collection, CancellationToken ct)
    {
        var indexes = new Dictionary<string, MongoIndex>(StringComparer.Ordinal);
        BsonDocument reply;
        try
        {
            reply = await client.RunCommandAsync(database, w => w.Write("listIndexes", collection), ct).ConfigureAwait(false);
        }
        catch (MongoCommandException e) when (e.Code == NamespaceNotFound)
        {
            return indexes; // no collection yet: createIndexes makes it
        }

        var cursor = client.Cursor(database, reply);
        await using (((IAsyncDisposable)cursor).ConfigureAwait(false)) // a cursor is enumerable too: the disposal's ConfigureAwait, named
        {
            await foreach (var index in cursor.WithCancellation(ct).ConfigureAwait(false))
            {
                if (!index.TryGetString("name", out var name) || !index.TryGetDocument("key", out var key))
                    continue;
                var unique = index.TryGetBoolean("unique", out var u) && u;
                string? live = null, open = null;
                if (index.TryGetDocument("partialFilterExpression", out var filter))
                {
                    // The two shapes this schema writes: { field: { $eq: false } } and { field: { $type: "null" } }.
                    foreach (var field in filter.Names())
                    {
                        if (!filter.TryGetDocument(field, out var condition))
                            continue;
                        if (condition.TryGetString("$type", out var type) && type == "null")
                            open = field;
                        else if (condition.TryGetBoolean("$eq", out var equals) && !equals)
                            live = field;
                    }
                }

                indexes[name] = new MongoIndex(key.Names(), unique, live, open);
            }
        }

        return indexes;
    }
}
