using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     <c>MaxByAsync</c> and <c>MinByAsync</c>, the terminals EF Core translates to a query ordered by the key and limited to one row: the
///     row with the largest (smallest) key, <c>default</c> when no row matches. Rows that tie on the key come in the order of the entity's key,
///     so the row read is the same on every run.
/// </summary>
[Collection("databases")]
public class MaxByMinByTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Fact]
    public async Task MaxBy_and_MinBy_order_by_the_key_then_the_entitys_key_and_read_one_row()
    {
        var statements = new List<string?>();
        var hooks = new SqlExecutionHooks { Executing = (_, command, _) => statements.Add(command?.CommandText) };
        using var session = new SqlSession(databases.Open(SqlDialect.Sqlite), SqlDialect.Sqlite) { Hooks = hooks };

        await session.Query<Order>().Where(o => o.Status == OrderStatus.Open).MaxByAsync(o => o.Total);
        await session.Query<Order>().Where(o => o.Status == OrderStatus.Open).MinByAsync(o => o.Total);

        Assert.Equal(2, statements.Count);
        Assert.EndsWith("WHERE (t.\"Status\" = @p0) ORDER BY t.\"Total\" DESC, t.\"Id\" LIMIT 1", statements[0], StringComparison.Ordinal);
        Assert.EndsWith("WHERE (t.\"Status\" = @p0) ORDER BY t.\"Total\", t.\"Id\" LIMIT 1", statements[1], StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task MaxBy_and_MinBy_return_the_row_with_the_extreme_key(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);

        // A key that is a column, after a Where: the open order with the largest total, the paid one created first.
        var open = seed.Orders.Where(o => o.Status == OrderStatus.Open).ToList();
        var largest = await session.Query<Order>().Where(o => o.Status == OrderStatus.Open).MaxByAsync(o => o.Total);
        Assert.NotNull(largest);
        Assert.Equal(open.Max(o => o.Total), largest.Total);
        Assert.Contains(open, o => o.Id == largest.Id && o.Total == largest.Total);

        var paid = seed.Orders.Where(o => o.Status == OrderStatus.Paid).ToList();
        var first = await session.Query<Order>().Where(o => o.Status == OrderStatus.Paid).MinByAsync(o => o.CreatedAt);
        Assert.NotNull(first);
        Assert.Equal(paid.Min(o => o.CreatedAt), first.CreatedAt);
        Assert.Contains(paid, o => o.Id == first.Id);

        // A key that is a correlated count: the Italian customer with the most open orders, and the one with the fewest.
        var italians = seed.Customers.Where(c => c.Country == "IT").ToList();
        int OpenOrders(Customer c) => seed.Orders.Count(o => o.CustomerId == c.Id && o.Status == OrderStatus.Open);
        Assert.NotEqual(italians.Max(OpenOrders), italians.Min(OpenOrders)); // the key separates the customers, or the test proves nothing

        var busiest = await session.Query<Customer>().Where(c => c.Country == "IT").MaxByAsync(c => c.Orders.Count(o => o.Status == OrderStatus.Open));
        Assert.NotNull(busiest);
        Assert.Equal(italians.Max(OpenOrders), OpenOrders(italians.Single(c => c.Id == busiest.Id)));

        var quietest = await session.Query<Customer>().Where(c => c.Country == "IT").MinByAsync(c => c.Orders.Count(o => o.Status == OrderStatus.Open));
        Assert.NotNull(quietest);
        Assert.Equal(italians.Min(OpenOrders), OpenOrders(italians.Single(c => c.Id == quietest.Id)));

        // Ties on the key are broken by the entity's key: the same row on every run.
        var again = await session.Query<Customer>().Where(c => c.Country == "IT").MaxByAsync(c => c.Orders.Count(o => o.Status == OrderStatus.Open));
        Assert.Equal(busiest.Id, again!.Id);

        // No row: default, as on EF Core.
        Assert.Null(await session.Query<Customer>().Where(c => c.Country == "XX").MaxByAsync(c => c.CreatedAt));
        Assert.Null(await session.Query<Customer>().Where(c => c.Country == "XX").MinByAsync(c => c.Orders.Count()));
    }
}
