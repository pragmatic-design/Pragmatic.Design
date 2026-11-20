using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Schema;

/// <summary>
///     MongoDB: a collection's ranges (#900) as its validator, a <c>$expr</c> each document written must satisfy, which the server refuses a write
///     against (<c>DocumentValidationFailure</c>, 121). Compared with the one <c>listCollections</c> reports, set with <c>collMod</c> when it
///     differs.
/// </summary>
/// <remarks>
///     The validator is the schema's for a collection that declares a range: one it finds there in another shape is replaced. The end may be null,
///     which the engine stores as null, never as a missing field; <c>$ifNull</c> reads a missing one as null all the same.
/// </remarks>
public static partial class DocSchema
{
    private static async Task<List<DocSchemaChange>> EnsureMongoRangesAsync(MongoClient client, string database, string collection, IReadOnlyList<DocRange> ranges,
        CancellationToken ct)
    {
        var existing = await ExistingRangesAsync(client, database, collection, ct).ConfigureAwait(false);
        var declared = ranges.Select(r => (r.From, r.To)).ToList();
        if (existing is not null && existing.SequenceEqual(declared))
            return [.. ranges.Select(r => new DocSchemaChange(collection, r.Name, DocSchemaOutcome.Unchanged))];

        try
        {
            await client.RunCommandAsync(database, w =>
            {
                w.Write("collMod", collection);
                WriteValidator(w, ranges);
                w.Write("validationLevel", "strict");
                w.Write("validationAction", "error");
            }, ct).ConfigureAwait(false);
        }
        catch (MongoCommandException e) when (e.Code == NamespaceNotFound)
        {
            // No collection yet (no index created it): created with the validator.
            await client.RunCommandAsync(database, w =>
            {
                w.Write("create", collection);
                WriteValidator(w, ranges);
            }, ct).ConfigureAwait(false);
        }
        var outcome = existing is null ? DocSchemaOutcome.Created : DocSchemaOutcome.Updated;
        return [.. ranges.Select(r => new DocSchemaChange(collection, r.Name, outcome))];
    }

    /// <summary><c>{ $expr: { $and: [ { $or: [ { $eq: [ { $ifNull: ["$To", null] }, null ] }, { $gte: ["$To", "$From"] } ] }, … ] } }</c>.</summary>
    private static void WriteValidator(BsonWriter w, IReadOnlyList<DocRange> ranges)
    {
        w.StartDocument("validator");
        w.StartDocument("$expr");
        w.StartArray("$and");
        for (var i = 0; i < ranges.Count; i++)
        {
            w.StartDocument(i);
            w.StartArray("$or");
            w.StartDocument(0);
            w.StartArray("$eq");
            w.StartDocument(0);
            w.StartArray("$ifNull");
            w.Write(0, "$" + ranges[i].To);
            w.WriteNull(1);
            w.EndArray();
            w.EndDocument();
            w.WriteNull(1);
            w.EndArray();
            w.EndDocument();
            w.StartDocument(1);
            w.StartArray("$gte");
            w.Write(0, "$" + ranges[i].To);
            w.Write(1, "$" + ranges[i].From);
            w.EndArray();
            w.EndDocument();
            w.EndArray();
            w.EndDocument();
        }

        w.EndArray();
        w.EndDocument();
        w.EndDocument();
    }

    /// <summary>
    ///     The ranges of the validator the collection has, as (from, to) in order, read back from the shape <see cref="WriteValidator" /> writes;
    ///     null when it has none, or one in another shape, or the collection does not exist yet.
    /// </summary>
    private static async Task<List<(string From, string To)>?> ExistingRangesAsync(MongoClient client, string database, string collection, CancellationToken ct)
    {
        var reply = await client.RunCommandAsync(database, w =>
        {
            w.Write("listCollections", 1);
            w.StartDocument("filter");
            w.Write("name", collection);
            w.EndDocument();
        }, ct).ConfigureAwait(false);

        var cursor = client.Cursor(database, reply);
        await using (((IAsyncDisposable)cursor).ConfigureAwait(false))
        {
            await foreach (var found in cursor.WithCancellation(ct).ConfigureAwait(false))
            {
                if (!found.TryGetDocument("options", out var options) || !options.TryGetDocument("validator", out var validator)
                    || !validator.TryGetDocument("$expr", out var expr) || !expr.TryGetDocument("$and", out var all))
                    return null;
                var ranges = new List<(string, string)>();
                foreach (var position in all.Names())
                {
                    if (!all.TryGetDocument(position, out var range) || !range.TryGetDocument("$or", out var either) || !either.TryGetDocument("1", out var after)
                        || !after.TryGetDocument("$gte", out var operands) || !operands.TryGetString("0", out var to) || !operands.TryGetString("1", out var from))
                        return null;
                    ranges.Add((from.TrimStart('$'), to.TrimStart('$')));
                }

                return ranges;
            }
        }

        return null;
    }
}
