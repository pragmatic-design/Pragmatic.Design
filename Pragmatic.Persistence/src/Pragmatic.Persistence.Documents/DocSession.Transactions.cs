using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     A multi-document transaction on MongoDB (a replica set or a sharded cluster; a standalone server refuses it): every command the session
///     sends while it is open carries the logical session (<c>lsid</c>), its <c>txnNumber</c> and <c>autocommit: false</c>, the first one
///     <c>startTransaction</c>; <see cref="CommitAsync" /> sends <c>commitTransaction</c>, disposing without it <c>abortTransaction</c>, then
///     the logical session ends. One transaction at a time per session, its commands one after the other. Cosmos DB: not translated (refused).
/// </summary>
public sealed class DocTransaction : IAsyncDisposable
{
    private readonly DocSession _session;
    private readonly Guid _id = Guid.NewGuid();
    private bool _started;
    private bool _finished;

    internal DocTransaction(DocSession session) => _session = session;

    /// <summary>The transaction's fields, appended to a command (after its own, before <c>$db</c>).</summary>
    internal void Write(BsonWriter w)
    {
        Session(w);
        w.Write("txnNumber", 1L); // one transaction per logical session
        if (!_started)
        {
            w.Write("startTransaction", true);
            _started = true;
        }

        w.Write("autocommit", false);
    }

    private void Session(BsonWriter w)
    {
        w.StartDocument("lsid");
        w.Write("id", _id);
        w.EndDocument();
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        if (_started)
            await EndAsync("commitTransaction", ct).ConfigureAwait(false);
        await FinishAsync(ct).ConfigureAwait(false);
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        if (_started)
        {
            try
            {
                await EndAsync("abortTransaction", ct).ConfigureAwait(false);
            }
            catch (MongoCommandException e) when (e.Code == NoSuchTransaction)
            {
                AbortedByServer = true; // what this abort wants, done already: the failure that made the server abort is the caller's to see
            }
        }

        await FinishAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     True when the rollback found the transaction aborted by the server already: a write it refused (a duplicate key) ends the transaction
    ///     with the error, and the abort that follows is answered NoSuchTransaction.
    /// </summary>
    public bool AbortedByServer { get; private set; }

    /// <summary>The server's answer to an abort of a transaction it aborted itself.</summary>
    private const int NoSuchTransaction = 251;

    /// <summary>Not committed: aborted (what an exception between the writes leaves).</summary>
    public async ValueTask DisposeAsync()
    {
        if (!_finished)
            await RollbackAsync().ConfigureAwait(false);
    }

    private Task<BsonDocument> EndAsync(string command, CancellationToken ct)
        => _session.MongoClient.RunCommandAsync("admin", w =>
        {
            w.Write(command, 1);
            Session(w);
            w.Write("txnNumber", 1L);
            w.Write("autocommit", false);
        }, ct);

    /// <summary>The session's commands go on outside a transaction; the server forgets the logical session.</summary>
    private async Task FinishAsync(CancellationToken ct)
    {
        _finished = true;
        _session.Ended(this);
        await _session.MongoClient.RunCommandAsync("admin", w =>
        {
            w.StartArray("endSessions");
            w.StartDocument(0);
            w.Write("id", _id);
            w.EndDocument();
            w.EndArray();
        }, ct).ConfigureAwait(false);
    }
}

public sealed partial class DocSession
{
    /// <summary>The open transaction, whose fields every command carries; null outside one.</summary>
    public DocTransaction? Transaction { get; private set; }

    /// <summary>Opens a multi-document transaction (MongoDB on a replica set): commit it, or dispose it to abort.</summary>
    public DocTransaction BeginTransaction()
    {
        if (Cosmos is not null)
            throw new NotSupportedException("A transaction on Cosmos DB (a transactional batch in one partition) is not translated by this version.");
        if (Transaction is not null)
            throw new InvalidOperationException("The session has a transaction open: commit or dispose it first.");
        return Transaction = new DocTransaction(this);
    }

    internal void Ended(DocTransaction transaction)
    {
        if (ReferenceEquals(Transaction, transaction))
            Transaction = null;
    }

    /// <summary>A command on the session's database, in the open transaction when there is one.</summary>
    internal Task<BsonDocument> RunAsync(Action<BsonWriter> build, CancellationToken ct)
    {
        var transaction = Transaction;
        return transaction is null ? MongoClient.RunCommandAsync(Database, build, ct) : MongoClient.RunCommandAsync(Database, w => { build(w); transaction.Write(w); }, ct);
    }

    /// <summary>The cursor of a reply, its getMore and killCursors in the open transaction when there is one.</summary>
    internal MongoCursor Cursor(BsonDocument reply) => MongoClient.Cursor(Database, reply, session: Transaction is { } transaction ? transaction.Write : null);
}
