using Microsoft.Data.Sqlite;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>The library's session pool: a returned session is rented again with its prepared commands; one left in a transaction is not.</summary>
[Collection("databases")]
public class SessionPoolTests(TestDatabases databases)
{
    [Fact]
    public async Task A_returned_session_is_rented_again_and_keeps_its_prepared_command()
    {
        await using var pool = new SqlSessionPool(() => new SqliteConnection(databases.ConnectionString(SqlDialect.Sqlite)), SqlDialect.Sqlite, capacity: 2);
        var orderId = databases.Seed.Orders[4321].Id;

        SqlSession first;
        using (var lease = await pool.RentAsync())
        {
            first = lease.Session;
            Assert.NotNull(await first.Query<Order>().FirstOrDefaultAsync((o, orderId) => o.Id == orderId, orderId));
        }

        Assert.Equal(1, pool.Idle);
        using (var lease = await pool.RentAsync())
        {
            Assert.Same(first, lease.Session); // the same session, so the plan finds its prepared command
            Assert.Equal(0, pool.Idle);
            Assert.NotNull(await lease.Session.Query<Order>().FirstOrDefaultAsync((o, orderId) => o.Id == orderId, orderId));
        }

        // Two rented at once are two sessions; both come back; a third returned beyond the capacity is disposed.
        var a = await pool.RentAsync();
        var b = await pool.RentAsync();
        var c = await pool.RentAsync();
        Assert.NotSame(a.Session, b.Session);
        a.Dispose();
        b.Dispose();
        c.Dispose();
        Assert.Equal(2, pool.Idle);

        // A session returned with a transaction open is not kept.
        using (var lease = await pool.RentAsync())
            lease.Session.BeginTransaction();
        Assert.Equal(1, pool.Idle);
    }
}
