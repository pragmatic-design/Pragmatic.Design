using System.Data.Common;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     Execution hooks (<see cref="SqlExecutionHooks" />): a callback before each statement with its command and origin, one after it with
///     the time it took and the error when it failed; on a plan's reads, on the writer's statements, on a write batch; a pool hands them to
///     every session; a session without hooks runs as before.
/// </summary>
[Collection("databases")]
public class HooksTests(TestDatabases databases)
{
    [Fact]
    public async Task Hooks_see_every_statement_with_its_origin_time_and_error()
    {
        var executing = new List<(string Origin, string? Sql)>();
        var executed = new List<(string Origin, TimeSpan Elapsed, Exception? Error)>();
        var hooks = new SqlExecutionHooks
        {
            Executing = (session, command, origin) => executing.Add((origin, command?.CommandText)),
            Executed = (session, origin, elapsed, error) => executed.Add((origin, elapsed, error)),
        };
        using var session = new SqlSession(databases.Open(SqlDialect.Sqlite), SqlDialect.Sqlite) { Hooks = hooks };
        var customer = databases.Seed.BusyCustomer;

        // A plan: the command with its text, the origin as the plan's call site, the time from the start to the last row.
        var count = await session.Query<Order>().CountAsync((o, id) => o.CustomerId == id && !o.IsDeleted, customer.Id);
        Assert.True(count > 0);
        var (origin, sql) = Assert.Single(executing);
        Assert.Contains("HooksTests.cs(", origin, StringComparison.Ordinal);
        Assert.StartsWith("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        var done = Assert.Single(executed);
        Assert.Equal(origin, done.Origin);
        Assert.True(done.Elapsed > TimeSpan.Zero);
        Assert.Null(done.Error);

        // The writer: the verb and the table as the origin; a batch of writes under its own.
        var orderId = databases.Seed.Orders[7].Id;
        var row = new Event { Id = Guid.NewGuid(), OrderId = orderId, Kind = "hooked", At = DateTimeOffset.UtcNow, Version = 1 };
        await session.InsertAsync(row);
        Assert.Equal("insert \"Events\"", executing[^1].Origin);
        await session.WriteBatch().Delete(row).ExecuteAsync();
        Assert.Equal("write batch", executing[^1].Origin);
        Assert.Contains("DELETE FROM", executing[^1].Sql, StringComparison.Ordinal);

        // A failure: the error reaches the hook, the exception reaches the caller.
        await session.InsertAsync(row);
        var failed = await Assert.ThrowsAnyAsync<DbException>(() => session.InsertAsync(row)); // the same key twice
        Assert.Same(failed, executed[^1].Error);
        Assert.Equal("insert \"Events\"", executed[^1].Origin);
        await session.DeleteAsync(row);
        Assert.Equal(executing.Count, executed.Count);

        // A pool hands the hooks to the sessions it opens.
        var pool = new SqlSessionPool(() => databases.Connect(SqlDialect.Sqlite), SqlDialect.Sqlite, hooks: hooks);
        using (var rented = await pool.RentAsync())
            Assert.Same(hooks, rented.Session.Hooks);
    }
}
