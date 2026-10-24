using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Documents.Tests;

/// <summary>
///     Multi-document transactions on a MongoDB replica set: writes inside one are invisible to another session until the commit, a disposed
///     transaction aborts them, reads inside see the transaction's own writes (a find, a count, a command by filter), and the server refuses
///     a transaction where the server would (the replica set is the deployment that has them). The fixture's start is checked first.
/// </summary>
[Collection("mongo-rs")]
public class DocumentTransactionTests(MongoReplicaSet mongo)
{
    // Any start that failed: the gate refuses to run this suite without Docker, so no failure here is Docker's absence.
    [Fact]
    public void The_replica_set_started()
        => Assert.Null(mongo.Unavailable);

    [Fact]
    public async Task A_replica_set_says_it_runs_transactions()
    {
        if (mongo.Unavailable is not null)
            return;
        Assert.True(await new DocSession(mongo.Client, MongoServer.Database()).SupportsTransactionsAsync());
    }

    [Fact]
    public async Task A_transaction_commits_or_aborts_its_writes_together()
    {
        if (mongo.Unavailable is not null)
            return;
        var database = MongoServer.Database();
        var session = new DocSession(mongo.Client, database);
        var outside = new DocSession(mongo.Client, database);
        var customer = Guid.NewGuid();
        await session.InsertAsync(New(customer, "ORD-T0")); // creates the collection outside a transaction

        // Committed: two inserts and an update by filter, seen inside before the commit, outside only after it.
        await using (var transaction = session.BeginTransaction())
        {
            await session.InsertAsync(New(customer, "ORD-T1"));
            await session.InsertAsync(New(customer, "ORD-T2"));
            Assert.Equal(3, await session.Query<OrderDocument>().CountAsync((o, c) => o.CustomerId == c, customer));
            Assert.Equal(1, await outside.Query<OrderDocument>().CountAsync((o, c) => o.CustomerId == c, customer));
            Assert.Equal(3, await session.Query<OrderDocument>().Where((o, c) => o.CustomerId == c, customer).ExecuteUpdateAsync(s => s.Set(o => o.Status, OrderStatus.Paid)));
            await transaction.CommitAsync();
        }

        Assert.Null(session.Transaction);
        Assert.Equal(3, await outside.Query<OrderDocument>().CountAsync((o, c) => o.CustomerId == c && o.Status == OrderStatus.Paid, customer));

        // Disposed without a commit: aborted, nothing of it stays.
        await using (session.BeginTransaction())
        {
            await session.InsertAsync(New(customer, "ORD-T3"));
            Assert.Equal(4, await session.Query<OrderDocument>().Where((o, c) => o.CustomerId == c, customer).ExecuteDeleteAsync()); // the three committed and its own insert
            Assert.Empty(await session.Query<OrderDocument>().Where((o, c) => o.CustomerId == c && o.Number != "ORD-T3", customer).ToListAsync());
        }

        Assert.Equal(["ORD-T0", "ORD-T1", "ORD-T2"], (await outside.Query<OrderDocument>().Where((o, c) => o.CustomerId == c, customer).OrderBy(o => o.Number).ToListAsync()).Select(o => o.Number));
        Assert.Throws<InvalidOperationException>(() => { session.BeginTransaction(); session.BeginTransaction(); });
    }

    private static OrderDocument New(Guid customer, string number) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = customer,
        Number = number,
        Status = OrderStatus.Open,
        Total = 12.5m,
        CreatedAt = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero),
    };
}
