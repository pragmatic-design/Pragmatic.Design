using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     A batch of declared queries: the generated batch overloads record each call, <c>ExecuteAsync</c> runs them in one
///     command and each item's <c>Result</c> is what the query alone answers, on the three databases.
/// </summary>
[Collection("databases")]
public class BatchTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_batch_of_declared_queries_answers_each_as_the_query_alone_does(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        var orderId = seed.Orders[4321].Id;
        var customerId = seed.BusyCustomer.Id;
        using var session = databases.Session(dialect);

        // Six queries of five shapes (a row, a projected page, a count, an aggregate, a list with a join, a dictionary) in one command.
        var batch = session.Batch();
        var byId = batch.ById(orderId);
        var rows = batch.OpenRows(customerId, 2, 20);
        var count = batch.CountOpenAbove(1000m);
        var total = batch.TotalOf(customerId);
        var search = batch.Search("10042");
        var five = batch.FirstFiveById(customerId);
        var withLines = batch.WithLines(customerId);
        Assert.Throws<InvalidOperationException>(() => rows.Result); // not executed yet
        await batch.ExecuteAsync();

        Assert.Equal((await session.ById(orderId))!.Number, byId.Result!.Number);
        Assert.Equal((await session.OpenRows(customerId, 2, 20)).Select(r => (r.Id, r.CustomerName)), rows.Result.Select(r => (r.Id, r.CustomerName)));
        Assert.Equal(await session.CountOpenAbove(1000m), count.Result);
        Assert.Equal(await session.TotalOf(customerId), total.Result);
        Assert.Equal((await session.Search("10042")).Select(o => (o.Id, o.Customer!.Name)), search.Result.Select(o => (o.Id, o.Customer!.Name)));
        Assert.Equal((await session.FirstFiveById(customerId)).Keys.Order(), five.Result.Keys.Order());
        // A collection include inside a batch: the companion statement's result set follows the rows' own, then the next item's.
        Assert.Equal(10, withLines.Result.Count);
        Assert.All(withLines.Result, o => Assert.Equal(Seed.LinesPerOrder, o.Lines.Count));
        Assert.Equal((await session.WithLines(customerId)).Select(o => o.Id), withLines.Result.Select(o => o.Id));

        // The same shape again with other values: the batch command is reused (prepared once per session), the results follow the values.
        var again = session.Batch();
        var otherRows = again.OpenRows(seed.Customers[3].Id, 1, 5);
        var otherCount = again.CountOpenAbove(5000m);
        await again.ExecuteAsync();
        Assert.Equal((await session.OpenRows(seed.Customers[3].Id, 1, 5)).Select(r => r.Id), otherRows.Result.Select(r => r.Id));
        Assert.Equal(await session.CountOpenAbove(5000m), otherCount.Result);

        // An IN list expands the statement per call: not in a batch (the command is prepared once), except on PostgreSQL, where the list is one array parameter.
        var withList = session.Batch();
        if (dialect == SqlDialect.PostgreSql)
        {
            var names = withList.NamesIn(["IT", "FR"]);
            await withList.ExecuteAsync();
            Assert.Equal(await session.NamesIn(["IT", "FR"]), names.Result);
        }
        else
            Assert.Throws<NotSupportedException>(() => withList.NamesIn(["IT", "FR"]));
        // An empty batch runs nothing.
        await session.Batch().ExecuteAsync();
    }
}
