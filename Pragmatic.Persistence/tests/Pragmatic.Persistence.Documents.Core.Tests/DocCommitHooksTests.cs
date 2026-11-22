using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     What a session's <see cref="DocSession.CommitHooks" /> see, on a MongoDB replica set: the documents before the command is sent, and
///     once their commit succeeded. A write outside a transaction is its own commit; inside one the documents wait for its commit, and an abort
///     forgets them. Throwing from <see cref="DocCommitHooks.Writing" /> writes nothing; throwing from <see cref="DocCommitHooks.Committed" />
///     reaches the caller with the document already there.
/// </summary>
[Collection("mongo-rs")]
public class DocCommitHooksTests(MongoReplicaSet mongo)
{
    [Fact]
    public async Task A_write_outside_a_transaction_is_reported_written_then_committed()
    {
        if (mongo.Unavailable is not null)
            return;
        var seen = new Seen();
        var session = Session(seen);
        var order = New(Guid.NewGuid(), "ORD-H0");

        await session.InsertAsync(order);
        await session.DeleteAsync(order);

        Assert.Equal([(order, DocWriteKind.Insert), (order, DocWriteKind.Delete)], seen.WritingDocuments());
        Assert.Equal([(order, DocWriteKind.Insert), (order, DocWriteKind.Delete)], seen.CommittedDocuments());
    }

    [Fact]
    public async Task A_write_in_a_transaction_is_committed_at_its_commit_and_never_after_an_abort()
    {
        if (mongo.Unavailable is not null)
            return;
        var seen = new Seen();
        var session = Session(seen);
        var customer = Guid.NewGuid();
        await session.InsertAsync(New(customer, "ORD-H1")); // creates the collection outside a transaction
        seen.Committed.Clear();
        var kept = New(customer, "ORD-H2");

        await using (var transaction = session.BeginTransaction())
        {
            await session.InsertAsync(kept);
            Assert.Empty(seen.Committed); // written, not committed yet
            await transaction.CommitAsync();
        }

        await using (session.BeginTransaction())
            await session.InsertAsync(New(customer, "ORD-H3")); // disposed open: aborted

        Assert.Equal([(kept, DocWriteKind.Insert)], seen.CommittedDocuments());
    }

    [Fact]
    public async Task Throwing_before_the_write_writes_nothing()
    {
        if (mongo.Unavailable is not null)
            return;
        var database = MongoServer.Database();
        var session = new DocSession(mongo.Client, database) { CommitHooks = new DocCommitHooks { Writing = _ => throw new InvalidOperationException("refused") } };
        var customer = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InsertAsync(New(customer, "ORD-H4")));

        Assert.Equal(0, await new DocSession(mongo.Client, database).Query<OrderDocument>().CountAsync((o, c) => o.CustomerId == c, customer));
    }

    [Fact]
    public async Task Throwing_after_the_commit_reaches_the_caller_with_the_document_there()
    {
        if (mongo.Unavailable is not null)
            return;
        var database = MongoServer.Database();
        var session = new DocSession(mongo.Client, database);
        var customer = Guid.NewGuid();
        await session.InsertAsync(New(customer, "ORD-H5")); // creates the collection outside a transaction
        session.CommitHooks = new DocCommitHooks { Committed = _ => throw new InvalidOperationException("acknowledgement lost") };

        await using (var transaction = session.BeginTransaction())
        {
            await session.InsertAsync(New(customer, "ORD-H6"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync());
        }

        Assert.Null(session.Transaction);
        Assert.Equal(2, await new DocSession(mongo.Client, database).Query<OrderDocument>().CountAsync((o, c) => o.CustomerId == c, customer));
    }

    private DocSession Session(Seen seen)
        => new(mongo.Client, MongoServer.Database()) { CommitHooks = new DocCommitHooks { Writing = seen.Writing.Add, Committed = seen.Committed.Add } };

    private static OrderDocument New(Guid customer, string number) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = customer,
        Number = number,
        Status = OrderStatus.Open,
        Total = 12.5m,
        CreatedAt = new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero),
    };

    private sealed class Seen
    {
        public List<IReadOnlyList<DocWrittenDocument>> Writing { get; } = [];

        public List<IReadOnlyList<DocWrittenDocument>> Committed { get; } = [];

        public List<(object, DocWriteKind)> WritingDocuments() => [.. Writing.SelectMany(d => d).Select(d => (d.Entity, d.Kind))];

        public List<(object, DocWriteKind)> CommittedDocuments() => [.. Committed.SelectMany(d => d).Select(d => (d.Entity, d.Kind))];
    }
}
