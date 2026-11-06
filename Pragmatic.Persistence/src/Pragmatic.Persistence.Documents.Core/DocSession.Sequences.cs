using System.Net;
using System.Text;
using System.Text.Json;
using Pragmatic.Persistence.Documents.Mongo;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The next values of a named counter: what an <c>[GeneratedValue]</c> <c>{SEQ}</c> number reads on the documents engine (#562). One document
///     per counter in <see cref="SequencesCollection" />, incremented atomically by the server, created by the first use. Values are unique and
///     increasing with gaps, as a database sequence's: a counter taken by a save that then fails is not handed out again. The increment runs
///     outside the session's transaction, as a sequence's does.
/// </summary>
public sealed partial class DocSession
{
    /// <summary>The collection of the counters, one document each, its id the counter's name.</summary>
    public const string SequencesCollection = "__pragmatic_sequences";

    private const string SequenceValue = "value";

    /// <summary>The counter's next value: 1 on its first use.</summary>
    public Task<long> NextValueAsync(string sequence, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequence);
        return Mongo is { } mongo ? MongoNextValueAsync(mongo, sequence, ct) : CosmosNextValueAsync(sequence, ct);
    }

    /// <summary><c>findAndModify</c> with <c>$inc</c>, an upsert: one round trip, atomic on the document.</summary>
    private async Task<long> MongoNextValueAsync(MongoClient mongo, string sequence, CancellationToken ct)
    {
        try
        {
            return await IncrementAsync().ConfigureAwait(false);
        }
        catch (MongoCommandException e) when (e.Code == DuplicateKey)
        {
            // Two first uses at once both upserted: the loser's insert hit the winner's _id. The document is there now, and the update finds it.
            return await IncrementAsync().ConfigureAwait(false);
        }

        async Task<long> IncrementAsync()
        {
            var reply = await mongo.RunCommandAsync(Database, w =>
            {
                w.Write("findAndModify", SequencesCollection);
                w.StartDocument("query");
                w.Write("_id", sequence);
                w.EndDocument();
                w.StartDocument("update");
                w.StartDocument("$inc");
                w.Write(SequenceValue, 1L);
                w.EndDocument();
                w.EndDocument();
                w.Write("new", true);
                w.Write("upsert", true);
            }, ct).ConfigureAwait(false);
            if (!reply.TryGetDocument("value", out var counter) || !counter.TryGetInt64(SequenceValue, out var value))
                throw new InvalidOperationException($"Counter '{sequence}': findAndModify returned no value.");
            return value;
        }
    }

    private const int DuplicateKey = 11000;

    /// <summary>
    ///     The counter's document created when missing (a conflict is another first use, which created it), then patched with <c>incr</c>, which
    ///     Cosmos DB applies atomically and answers with the document after it.
    /// </summary>
    private async Task<long> CosmosNextValueAsync(string sequence, CancellationToken ct)
    {
        await CosmosClient.CreateDatabaseIfNotExistsAsync(Database, ct).ConfigureAwait(false);
        await CosmosClient.CreateContainerIfNotExistsAsync(Database, SequencesCollection, "/id", null, ct).ConfigureAwait(false);
        var collection = $"dbs/{Database}/colls/{SequencesCollection}";
        var partitionKey = Encoding.UTF8.GetString(Json(w =>
        {
            w.WriteStartArray();
            w.WriteStringValue(sequence);
            w.WriteEndArray();
        }));

        var created = Json(w =>
        {
            w.WriteStartObject();
            w.WriteString("id", sequence);
            w.WriteNumber(SequenceValue, 0);
            w.WriteEndObject();
        });
        await CosmosClient.SendAsync(HttpMethod.Post, "docs", collection, collection + "/docs", created,
            h => h.TryAddWithoutValidation("x-ms-documentdb-partitionkey", partitionKey), ct: ct, accepted: HttpStatusCode.Conflict).ConfigureAwait(false);

        var link = $"{collection}/docs/{Uri.EscapeDataString(sequence)}";
        var patch = Encoding.UTF8.GetBytes($"{{\"operations\":[{{\"op\":\"incr\",\"path\":\"/{SequenceValue}\",\"value\":1}}]}}");
        var response = await CosmosClient.SendAsync(HttpMethod.Patch, "docs", link, link, patch,
            h => h.TryAddWithoutValidation("x-ms-documentdb-partitionkey", partitionKey), "application/json_patch+json", ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(response.Body);
        return document.RootElement.GetProperty(SequenceValue).GetInt64();
    }

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
            write(w);
        return stream.ToArray();
    }
}
