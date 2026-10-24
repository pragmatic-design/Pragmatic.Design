using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>Declared commands, for the batch: an update and a delete by filter, and a count to read around them.</summary>
public static partial class CommandQueries
{
    [Query]
    private static ValueTask<int> CountOpenOf(SqlQuery<Order> orders, Guid customerId)
        => orders.CountAsync(o => o.CustomerId == customerId && o.Status == OrderStatus.Open);

    [Query]
    private static ValueTask<int> MarkOpenPaid(SqlQuery<Order> orders, Guid customerId)
        => orders.Where(o => o.CustomerId == customerId && o.Status == OrderStatus.Open).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Paid));

    [Query]
    private static ValueTask<int> DeleteLinesOf(SqlQuery<OrderLine> lines, Guid orderId)
        => lines.Where(l => l.OrderId == orderId).ExecuteDeleteAsync();
}

/// <summary>
///     Commands in a batch: each joins in the form that answers its rows touched as a result set (RETURNING 1 on SQLite and PostgreSQL, the
///     count after it on SQL Server and MySQL, the PL/SQL block's count on Oracle), so every item reads its own count and the statements run in
///     order. In a transaction, rolled back.
/// </summary>
[Collection("databases")]
public class BatchCommandTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Commands_in_a_batch_answer_their_rows_touched_in_order(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        var seed = databases.Seed;
        var customerId = seed.BusyCustomer.Id;
        var open = seed.Orders.Count(o => o.CustomerId == customerId && o.Status == OrderStatus.Open);
        var orderId = seed.Orders.First(o => o.CustomerId == customerId).Id;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();

        var batch = session.Batch();
        var before = batch.CountOpenOf(customerId);
        var paid = batch.MarkOpenPaid(customerId);
        var after = batch.CountOpenOf(customerId);
        var deleted = batch.DeleteLinesOf(orderId);
        var again = batch.MarkOpenPaid(customerId);
        await batch.ExecuteAsync();

        Assert.True(open > 0);
        Assert.Equal(open, before.Result);
        Assert.Equal(open, paid.Result);
        Assert.Equal(0, after.Result); // the update ran before the second count
        Assert.Equal(Seed.LinesPerOrder, deleted.Result);
        Assert.Equal(0, again.Result); // its own count, not the batch's sum
        transaction.Rollback();
    }
}
