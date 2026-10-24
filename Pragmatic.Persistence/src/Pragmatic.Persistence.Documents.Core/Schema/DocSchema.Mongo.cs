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
                    if (index.LiveField is { } live)
                    {
                        w.StartDocument("partialFilterExpression");
                        w.StartDocument(live);
                        w.Write("$eq", false);
                        w.EndDocument();
                        w.EndDocument();
                    }

                    w.EndDocument();
                    w.EndArray();
                }, ct).ConfigureAwait(false);
                changes.Add(new DocSchemaChange(collection.Collection, index.Name, outcome));
            }
        }

        return new DocSchemaReport(changes);
    }

    /// <summary>An index as <c>listIndexes</c> reports it: its key's fields in order, unique, the live field of its partial filter.</summary>
    private sealed record MongoIndex(List<string> Fields, bool Unique, string? LiveField)
    {
        public bool Matches(DocIndex declared)
            => Fields.SequenceEqual(declared.Fields, StringComparer.Ordinal) && Unique == declared.Unique && LiveField == declared.LiveField;
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
                string? live = null;
                if (index.TryGetDocument("partialFilterExpression", out var filter) && filter.Names() is [var field])
                    live = field; // the one shape this schema writes: { field: { $eq: false } }
                indexes[name] = new MongoIndex(key.Names(), unique, live);
            }
        }

        return indexes;
    }
}
