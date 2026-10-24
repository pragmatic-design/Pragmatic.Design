using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Mongo;

/// <summary>
///     The documents of a <c>find</c> or <c>aggregate</c>: the reply's <c>firstBatch</c>, then <c>getMore</c> for each next batch while the
///     server's cursor id is not 0. Each document is a slice of its batch's bytes (<see cref="BsonDocument.Bytes" />), for a generated reader.
///     Disposing it before the end sends <c>killCursors</c>, so the server does not keep the cursor until its timeout.
/// </summary>
public sealed class MongoCursor : IAsyncEnumerable<BsonDocument>, IAsyncDisposable
{
    private readonly MongoClient _client;
    private readonly string _database;
    private readonly int? _batchSize;
    private readonly string _collection;
    private readonly Action<BsonWriter>? _session;
    private List<BsonDocument> _batch;
    private long _id;

    internal MongoCursor(MongoClient client, string database, BsonDocument reply, int? batchSize, Action<BsonWriter>? session = null)
    {
        _client = client;
        _database = database;
        _batchSize = batchSize;
        _session = session;
        if (!reply.TryGetDocument("cursor", out var cursor) || !cursor.TryGetInt64("id", out _id) || !cursor.TryGetString("ns", out var ns) || !cursor.TryGetDocument("firstBatch", out var first))
            throw new MongoException("The reply holds no cursor (cursor.id, cursor.ns, cursor.firstBatch).");
        _collection = ns[(ns.IndexOf('.', StringComparison.Ordinal) + 1)..];
        _batch = first.Documents();
    }

    /// <summary>True while the server still holds more batches.</summary>
    public bool Open => _id != 0;

    public async IAsyncEnumerator<BsonDocument> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            foreach (var document in _batch)
                yield return document;
            if (_id == 0)
                yield break;
            var id = _id;
            var reply = await _client.RunCommandAsync(_database, w =>
            {
                w.Write("getMore", id);
                w.Write("collection", _collection);
                if (_batchSize is { } size)
                    w.Write("batchSize", size);
                _session?.Invoke(w);
            }, cancellationToken).ConfigureAwait(false);
            if (!reply.TryGetDocument("cursor", out var cursor) || !cursor.TryGetInt64("id", out _id) || !cursor.TryGetDocument("nextBatch", out var next))
                throw new MongoException("The getMore reply holds no cursor (cursor.id, cursor.nextBatch).");
            _batch = next.Documents();
        }
    }

    /// <summary>Every document left, in a list.</summary>
    public async Task<List<BsonDocument>> ToListAsync(CancellationToken ct = default)
    {
        var all = new List<BsonDocument>();
        await foreach (var document in this.WithCancellation(ct).ConfigureAwait(false))
            all.Add(document);
        return all;
    }

    public async ValueTask DisposeAsync()
    {
        if (_id == 0)
            return;
        var id = _id;
        _id = 0;
        await _client.RunCommandAsync(_database, w =>
        {
            w.Write("killCursors", _collection);
            w.StartArray("cursors");
            w.Write(0, id);
            w.EndArray();
            _session?.Invoke(w);
        }).ConfigureAwait(false);
    }
}
