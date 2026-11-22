using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     What a session's <see cref="SqlSession.CommitHooks" /> see: the rows before they are written, and once their commit succeeded. A write
///     outside a transaction is its own commit; inside one the rows wait for its commit, and a rollback, whole or to a savepoint, forgets them.
///     Throwing from <see cref="SqlCommitHooks.Writing" /> writes nothing; throwing from <see cref="SqlCommitHooks.Committed" /> reaches the
///     caller with the row already there, which is what a lost acknowledgement is.
/// </summary>
[Collection("databases")]
public class CommitHooksTests(TestDatabases databases)
{
    private static Event NewEvent(Guid orderId, string kind) => new() { Id = Guid.NewGuid(), OrderId = orderId, Kind = kind, At = new DateTimeOffset(2026, 11, 22, 8, 0, 0, TimeSpan.Zero) };

    private SqlSession Session(Seen seen)
    {
        var session = databases.Session(SqlDialect.Sqlite);
        session.CommitHooks = new SqlCommitHooks { Writing = seen.Writing.Add, Committed = seen.Committed.Add };
        return session;
    }

    private static async Task<List<string>> Kinds(SqlSession session, Guid orderId)
        => await session.Query<Event>().Where((e, id) => e.OrderId == id, orderId).OrderBy(e => e.Kind).Select(e => e.Kind).ToListAsync();

    [Fact]
    public async Task A_write_outside_a_transaction_is_reported_written_then_committed()
    {
        var seen = new Seen();
        using var session = Session(seen);
        var row = NewEvent(Guid.NewGuid(), "alone");

        await session.InsertAsync(row);

        Assert.Equal([(row, SqlWriteKind.Insert)], seen.WritingRows());
        Assert.Equal([(row, SqlWriteKind.Insert)], seen.CommittedRows());
    }

    [Fact]
    public async Task A_write_in_a_transaction_is_committed_at_its_commit_and_never_after_a_rollback()
    {
        var seen = new Seen();
        using var session = Session(seen);
        var orderId = Guid.NewGuid();
        var kept = NewEvent(orderId, "kept");
        var undone = NewEvent(orderId, "undone");

        using (var transaction = session.BeginTransaction())
        {
            await session.InsertAsync(kept);
            Assert.Empty(seen.Committed); // written, not committed yet
            transaction.Commit();
        }

        using (var transaction = session.BeginTransaction())
        {
            await session.InsertAsync(undone);
            transaction.Rollback();
        }

        Assert.Equal([(kept, SqlWriteKind.Insert)], seen.CommittedRows());
        Assert.Equal(2, seen.Writing.Count); // both were about to be written
    }

    [Fact]
    public async Task A_transaction_disposed_open_reports_nothing_committed()
    {
        var seen = new Seen();
        using var session = Session(seen);

        using (session.BeginTransaction())
            await session.InsertAsync(NewEvent(Guid.NewGuid(), "abandoned"));

        Assert.Empty(seen.Committed);
    }

    [Fact]
    public async Task A_rollback_to_a_savepoint_forgets_the_rows_written_after_it()
    {
        var seen = new Seen();
        using var session = Session(seen);
        var orderId = Guid.NewGuid();
        var before = NewEvent(orderId, "before");
        var after = NewEvent(orderId, "after");
        var again = NewEvent(orderId, "again");

        using (var transaction = session.BeginTransaction())
        {
            await session.InsertAsync(before);
            transaction.Save("point");
            await session.InsertAsync(after);
            transaction.Rollback("point");
            await session.InsertAsync(again);
            transaction.Commit();
        }

        Assert.Equal(["again", "before"], await Kinds(session, orderId));
        Assert.Equal([(before, SqlWriteKind.Insert), (again, SqlWriteKind.Insert)], seen.CommittedRows());
    }

    [Fact]
    public async Task A_write_batch_reports_each_row_once_with_its_write()
    {
        var seen = new Seen();
        using var session = Session(seen);
        var orderId = Guid.NewGuid();
        var first = NewEvent(orderId, "first");
        var second = NewEvent(orderId, "second");

        await session.WriteBatch().Insert(first).Insert(second).ExecuteAsync();
        await session.WriteBatch().Delete(first).ExecuteAsync();

        Assert.Equal([(first, SqlWriteKind.Insert), (second, SqlWriteKind.Insert), (first, SqlWriteKind.Delete)], seen.CommittedRows());
        Assert.Equal(["second"], await Kinds(session, orderId));
    }

    [Fact]
    public async Task Throwing_before_the_write_writes_nothing()
    {
        var seen = new Seen();
        using var session = databases.Session(SqlDialect.Sqlite);
        session.CommitHooks = new SqlCommitHooks { Writing = _ => throw new InvalidOperationException("refused"), Committed = seen.Committed.Add };
        var orderId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InsertAsync(NewEvent(orderId, "refused")));

        Assert.Empty(await Kinds(session, orderId));
        Assert.Empty(seen.Committed);
    }

    [Fact]
    public async Task Throwing_after_the_commit_reaches_the_caller_with_the_row_there()
    {
        using var session = databases.Session(SqlDialect.Sqlite);
        session.CommitHooks = new SqlCommitHooks { Committed = _ => throw new InvalidOperationException("acknowledgement lost") };
        var orderId = Guid.NewGuid();

        using (var transaction = session.BeginTransaction())
        {
            await session.InsertAsync(NewEvent(orderId, "committed"));
            Assert.Throws<InvalidOperationException>(transaction.Commit);
        }

        Assert.Null(session.Transaction);
        session.CommitHooks = null;
        Assert.Equal(["committed"], await Kinds(session, orderId));
    }

    [Fact]
    public async Task No_hooks_cost_nothing_and_change_nothing()
    {
        using var session = databases.Session(SqlDialect.Sqlite);
        var orderId = Guid.NewGuid();

        await session.InsertAsync(NewEvent(orderId, "plain"));

        Assert.Equal(["plain"], await Kinds(session, orderId));
    }

    private sealed class Seen
    {
        public List<IReadOnlyList<SqlWrittenRow>> Writing { get; } = [];

        public List<IReadOnlyList<SqlWrittenRow>> Committed { get; } = [];

        public List<(object, SqlWriteKind)> WritingRows() => [.. Writing.SelectMany(rows => rows).Select(r => (r.Row, r.Kind))];

        public List<(object, SqlWriteKind)> CommittedRows() => [.. Committed.SelectMany(rows => rows).Select(r => (r.Row, r.Kind))];
    }
}
