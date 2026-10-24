using System.Data;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     Transactions and locks: an isolation level on BeginTransaction, savepoints, a command timeout, rows locked for the transaction
///     (<c>ForUpdate</c>: FOR UPDATE / UPDLOCK, SKIP LOCKED / READPAST as a work queue, NOWAIT), and a unit of work retried when the
///     database reports a transient failure (PostgreSQL under SERIALIZABLE: one of two concurrent transactions fails, the retry
///     brings it through).
/// </summary>
[Collection("databases")]
public class TransactionTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    private static Event NewEvent(Guid orderId, string kind) => new() { Id = Guid.NewGuid(), OrderId = orderId, Kind = kind, At = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero) };

    [Fact]
    public void ForUpdate_renders_the_dialect_lock()
    {
        var postgres = Session(SqlDialect.PostgreSql);
        Assert.EndsWith(" ORDER BY t.\"At\" LIMIT @p1 FOR UPDATE SKIP LOCKED", postgres.Query<Event>().Where(e => e.Kind == "x").OrderBy(e => e.At).Take(2).ForUpdate(SqlLockMode.SkipLocked).ToSql(), StringComparison.Ordinal);
        Assert.EndsWith(" FOR UPDATE", postgres.Query<Event>().Where(e => e.Kind == "x").ForUpdate().ToSql(), StringComparison.Ordinal);
        Assert.EndsWith(" FOR UPDATE NOWAIT", postgres.Query<Event>().ForUpdate(SqlLockMode.NoWait).ToSql(), StringComparison.Ordinal);
        var sqlServer = Session(SqlDialect.SqlServer);
        Assert.Contains(" FROM [Events] t WITH (UPDLOCK, ROWLOCK, READPAST) WHERE ", sqlServer.Query<Event>().Where(e => e.Kind == "x").OrderBy(e => e.At).Take(2).ForUpdate(SqlLockMode.SkipLocked).ToSql(), StringComparison.Ordinal);
        Assert.Contains(" FROM [Events] t WITH (UPDLOCK, ROWLOCK) WHERE ", sqlServer.Query<Event>().Where(e => e.Kind == "x").ForUpdate().ToSql(), StringComparison.Ordinal);
        Assert.Contains(" FROM [Events] t WITH (UPDLOCK, ROWLOCK, NOWAIT)", sqlServer.Query<Event>().ForUpdate(SqlLockMode.NoWait).ToSql(), StringComparison.Ordinal);
        // SQLite locks the database as a whole: the statement is the plain one (the emulation is reported at the call site, PRAG3012).
        Assert.DoesNotContain("UPDATE", Session(SqlDialect.Sqlite).Query<Event>().Where(e => e.Kind == "x").ForUpdate(SqlLockMode.SkipLocked).ToSql(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Isolation_savepoints_timeout_and_a_work_queue(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = new SqlSession(databases.Open(dialect), dialect) { CommandTimeout = 7 };
        Assert.Equal(7, session.Query<Order>().Where(o => o.IsDeleted).ToCommand().CommandTimeout);
        var orderId = Guid.NewGuid();

        // A savepoint inside a transaction at an isolation level every dialect knows: what came after it is undone, the rest commits.
        using (var transaction = session.BeginTransaction(IsolationLevel.Serializable))
        {
            Assert.Equal(IsolationLevel.Serializable, transaction.IsolationLevel);
            await session.InsertAsync(NewEvent(orderId, "kept"));
            transaction.Save("point");
            await session.InsertAsync(NewEvent(orderId, "undone"));
            transaction.Rollback("point");
            await session.InsertAsync(NewEvent(orderId, "kept too"));
            transaction.Commit();
        }

        Assert.Equal(["kept", "kept too"], (await session.Query<Event>().Where((e, id) => e.OrderId == id, orderId).OrderBy(e => e.Kind).Select(e => e.Kind).ToListAsync()));

        // A work queue: a worker takes two jobs and holds them for its transaction; another worker, skipping locked rows, takes the other two.
        // Ordered by the key: SQL Server locks every row it reads to order a TOP, so ordering by a column without an index would lock all four
        // (the second worker would see nothing). SQLite has no row locks: the second worker sees the same rows (a claim there is an UPDATE … RETURNING).
        var queue = Guid.NewGuid();
        var jobs = new[] { NewEvent(queue, "j1"), NewEvent(queue, "j2"), NewEvent(queue, "j3"), NewEvent(queue, "j4") };
        await session.InsertManyAsync(jobs);
        var ids = jobs.Select(j => j.Id).ToHashSet();
        using var worker = databases.Session(dialect);
        using (var holding = session.BeginTransaction())
        {
            var mine = await session.Query<Event>().Where((e, id) => e.OrderId == id, queue).OrderBy(e => e.Id).Take(2).ForUpdate(SqlLockMode.SkipLocked).Select(e => e.Id).ToListAsync();
            var theirs = await worker.Query<Event>().Where((e, id) => e.OrderId == id, queue).OrderBy(e => e.Id).Take(2).ForUpdate(SqlLockMode.SkipLocked).Select(e => e.Id).ToListAsync();
            Assert.Equal(2, mine.Count);
            Assert.Equal(2, theirs.Count);
            if (dialect == SqlDialect.Sqlite)
                Assert.Equal(mine, theirs);
            else
                Assert.Empty(mine.Intersect(theirs)); // disjoint: the four jobs taken once each
            Assert.True(mine.Concat(theirs).All(ids.Contains));
            holding.Commit();
        }

        // A unit of work in its own transaction: committed when it returns, rolled back when it throws.
        var done = await session.TransactAsync(async (s, ct) =>
        {
            await s.InsertAsync(NewEvent(orderId, "unit"), ct);
            return await s.Query<Event>().CountAsync((e, id) => e.OrderId == id, orderId, ct);
        }, new SqlTransactionOptions { Isolation = IsolationLevel.Serializable });
        Assert.Equal(3, done);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.TransactAsync(async (s, ct) =>
        {
            await s.InsertAsync(NewEvent(orderId, "never"), ct);
            throw new InvalidOperationException("the work failed for its own reasons: no retry, nothing kept");
        }));
        Assert.Null(session.Transaction);
        Assert.Equal(3, await session.Query<Event>().CountAsync((e, id) => e.OrderId == id, orderId));
    }

    [Fact]
    public async Task A_serialization_failure_on_postgresql_is_retried()
    {
        if (Skip(SqlDialect.PostgreSql))
            return;
        using var first = databases.Session(SqlDialect.PostgreSql);
        using var second = databases.Session(SqlDialect.PostgreSql);
        var orderId = Guid.NewGuid();
        var options = new SqlTransactionOptions { Isolation = IsolationLevel.Serializable, Attempts = 3 };
        var secondRead = new SemaphoreSlim(0);
        var firstCommitted = new SemaphoreSlim(0);
        var attempts = 0;

        // Write skew: both count the order's events, then each inserts one. Under SERIALIZABLE PostgreSQL lets the first commit and fails the
        // second (SQLSTATE 40001); the retry runs the second's work again, which now counts one more.
        var secondWork = second.TransactAsync(async (s, ct) =>
        {
            var attempt = ++attempts;
            var count = await s.Query<Event>().CountAsync((e, id) => e.OrderId == id, orderId, ct);
            if (attempt == 1)
            {
                secondRead.Release();
                await firstCommitted.WaitAsync(ct);
            }

            await s.InsertAsync(NewEvent(orderId, "second-saw-" + count), ct);
        }, options);
        await secondRead.WaitAsync();
        await first.TransactAsync(async (s, ct) =>
        {
            var count = await s.Query<Event>().CountAsync((e, id) => e.OrderId == id, orderId, ct);
            await s.InsertAsync(NewEvent(orderId, "first-saw-" + count), ct);
        }, options);
        firstCommitted.Release();
        await secondWork;

        Assert.Equal(2, attempts);
        Assert.Equal(["first-saw-0", "second-saw-1"], await first.Query<Event>().Where((e, id) => e.OrderId == id, orderId).OrderBy(e => e.Kind).Select(e => e.Kind).ToListAsync());
    }
}
