using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

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
