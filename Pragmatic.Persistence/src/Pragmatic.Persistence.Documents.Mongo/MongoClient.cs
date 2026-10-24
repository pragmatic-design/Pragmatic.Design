using System.Collections.Concurrent;
using Pragmatic.Persistence.Documents.Mongo.Bson;
using Pragmatic.Persistence.Documents.Mongo.Wire;

namespace Pragmatic.Persistence.Documents.Mongo;

/// <summary>
///     The driver's entry point: a pool of connections to one server and the commands run on them. A command is a BSON document whose first
///     element names it (<c>find</c>, <c>insert</c>…); <see cref="RunCommandAsync(string, Action{BsonWriter}, CancellationToken)" /> adds <c>$db</c>, takes a connection, sends it, checks the
///     reply (<c>ok</c>, <c>writeErrors</c>) and gives the connection back. A connection whose stream failed is dropped, not reused. Thread-safe.
/// </summary>
public sealed class MongoClient : IAsyncDisposable
{
    private readonly ConcurrentBag<MongoConnection> _idle = [];
    private readonly SemaphoreSlim _slots;
    private bool _disposed;

    public MongoClient(string connectionString) : this(MongoSettings.Parse(connectionString))
    {
    }

    public MongoClient(MongoSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        _slots = new SemaphoreSlim(settings.MaxPoolSize, settings.MaxPoolSize);
    }

    public MongoSettings Settings { get; }

    /// <summary>
    ///     Runs a command on <paramref name="database" />: <paramref name="build" /> writes its elements, the command's name first
    ///     (<c>w.Write("find", "orders")</c>), and the driver closes the document with <c>$db</c>. Throws <see cref="MongoCommandException" />
    ///     for <c>ok: 0</c> and <see cref="MongoWriteException" /> for a refused document.
    /// </summary>
    public Task<BsonDocument> RunCommandAsync(string database, Action<BsonWriter> build, CancellationToken ct = default)
        => RunCommandAsync(Encode(database, build), ct);

    /// <summary>
    ///     The command of <see cref="RunCommandAsync(string, Action{BsonWriter}, CancellationToken)" /> run synchronously, on the socket's and the
    ///     stream's synchronous calls: for what has to finish where no await is possible (a transaction's synchronous dispose).
    /// </summary>
    public BsonDocument RunCommand(string database, Action<BsonWriter> build)
        => Synchronous.Result(RunCommandCoreAsync(Encode(database, build), useAsync: false, CancellationToken.None));

    /// <summary>Runs a command already encoded (its <c>$db</c> included).</summary>
    public Task<BsonDocument> RunCommandAsync(ReadOnlyMemory<byte> command, CancellationToken ct = default) => RunCommandCoreAsync(command, useAsync: true, ct).AsTask();

    private static ReadOnlyMemory<byte> Encode(string database, Action<BsonWriter> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var writer = new BsonWriter();
        writer.StartDocument();
        build(writer);
        if (writer.Depth != 1)
            throw new InvalidOperationException("The command left a document or an array open.");
        writer.Write("$db", database);
        writer.EndDocument();
        return writer.Written;
    }

    private async ValueTask<BsonDocument> RunCommandCoreAsync(ReadOnlyMemory<byte> command, bool useAsync, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (useAsync)
            await _slots.WaitAsync(ct).ConfigureAwait(false);
        else
            _slots.Wait(ct);
        MongoConnection? connection = null;
        try
        {
            connection = _idle.TryTake(out var kept) ? kept : useAsync ? await MongoConnection.OpenAsync(Settings, ct).ConfigureAwait(false) : MongoConnection.Open(Settings);
            return await connection.CommandCoreAsync(command, useAsync, ct).ConfigureAwait(false);
        }
        finally
        {
            if (connection is not null)
            {
                if (connection.Healthy && !_disposed)
                    _idle.Add(connection);
                else if (useAsync)
                    await connection.DisposeAsync().ConfigureAwait(false);
                else
                    connection.Dispose();
            }

            _slots.Release();
        }
    }

    /// <summary>
    ///     A <c>find</c> or <c>aggregate</c> reply's cursor, read batch after batch (<c>getMore</c>) until the server closes it; disposing it early
    ///     kills it on the server. <paramref name="session" /> appends a logical session's fields (an open transaction's) to those commands.
    /// </summary>
    public MongoCursor Cursor(string database, BsonDocument reply, int? batchSize = null, Action<BsonWriter>? session = null) => new(this, database, reply, batchSize, session);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        while (_idle.TryTake(out var connection))
            await connection.DisposeAsync().ConfigureAwait(false);
        _slots.Dispose();
    }
}
