using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     Typed chains recorded on a batch at their call sites (<c>ToList(batch)</c>, <c>Count(batch)</c>, <c>FirstOrDefault(batch)</c>,
///     <c>Any(batch)</c>), beside a declared query: one command, each item what the chain alone answers, a projection into an anonymous type too.
/// </summary>
[Collection("databases")]
public class TypedBatchTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Typed_chains_join_a_batch_at_their_call_sites(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        var customerId = databases.Seed.BusyCustomer.Id;
        using var session = databases.Session(dialect);

        var batch = session.Batch();
        var page = session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number).Take(5).ToList(batch);
        var count = session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).Count(batch);
        var customer = session.Query<Customer>().Where((c, id) => c.Id == id, customerId).FirstOrDefault(batch);
        var none = session.Query<Order>().Where((o, n) => o.Number == n, "no such number").Any(batch);
        var numbers = session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderByDescending(o => o.Number).Take(3).Select(o => new { o.Number, o.Total }).ToList(batch);
        var declared = batch.CountOpenAbove(1000m);
        Assert.Throws<InvalidOperationException>(() => page.Result); // not run yet
        await batch.ExecuteAsync();

        Assert.Equal((await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number).Take(5).ToListAsync()).Select(o => o.Id), page.Result.Select(o => o.Id));
        Assert.Equal(await session.Query<Order>().CountAsync((o, id) => o.CustomerId == id, customerId), count.Result);
        Assert.Equal(databases.Seed.BusyCustomer.Name, customer.Result!.Name);
        Assert.False(none.Result);
        Assert.Equal((await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderByDescending(o => o.Number).Take(3).Select(o => o.Number).ToListAsync()), numbers.Result.Select(n => n.Number));
        Assert.Equal(await session.CountOpenAbove(1000m), declared.Result);
    }
}
