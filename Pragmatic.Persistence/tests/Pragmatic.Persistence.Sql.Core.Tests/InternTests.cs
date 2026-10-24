using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     <c>[Interned]</c> string columns: one instance per distinct value across the rows read (the entity's reader, a projection of the
///     column, the runtime path), from a bounded pool; the value itself is what the row holds.
/// </summary>
[Collection("databases")]
public class InternTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Fact]
    public void The_pool_shares_an_instance_per_value_and_stops_growing_at_its_capacity()
    {
        var pool = new SqlInternPool();
        var first = pool.Intern(new string('a', 3));
        Assert.Same(first, pool.Intern(new string('a', 3)));
        for (var i = 0; i < SqlInternPool.Capacity + 10; i++)
            pool.Intern(i.ToString());
        Assert.Equal(SqlInternPool.Capacity, pool.Count);
        var beyond = new string('z', 2);
        Assert.NotSame(beyond, pool.Intern(new string('z', 2))); // full: the value comes back as given, not kept
        Assert.Same(first, pool.Intern("aaa"));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Rows_share_the_interned_values_in_entities_projections_and_the_runtime_path(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var customer = seed.BusyCustomer;

        // The entity: every customer of a country holds the same Country instance; the names are right.
        var customers = await session.Query<Customer>().OrderBy(c => c.Name).Take(50).ToListAsync(); // by name: SQL Server orders uniqueidentifiers by its own byte order
        Assert.Equal(seed.Customers.OrderBy(c => c.Name, StringComparer.Ordinal).Take(50).Select(c => c.Name), customers.Select(c => c.Name));
        foreach (var group in customers.GroupBy(c => c.Country))
            Assert.All(group, c => Assert.Same(group.First().Country, c.Country));

        // A projection through a navigation: the page of one customer's orders carries one CustomerName instance.
        var rows = await session.Query<Order>().Where((o, id) => o.CustomerId == id && !o.IsDeleted, customer.Id).OrderBy(o => o.Number).Take(20).Select(o => new OrderRowDto { Id = o.Id, Number = o.Number, Total = o.Total, CustomerName = o.Customer!.Name }).ToListAsync();
        Assert.Equal(20, rows.Count);
        Assert.All(rows, r => Assert.Equal(customer.Name, r.CustomerName));
        Assert.All(rows, r => Assert.Same(rows[0].CustomerName, r.CustomerName));
        Assert.Same(rows[0].CustomerName, customers.FirstOrDefault(c => c.Id == customer.Id)?.Name ?? rows[0].CustomerName);

        // The runtime path reads through the same pool.
        var dynamic = await session.Query<Customer>().AsQueryable().Where(c => c.Id == customer.Id).ToListAsync();
        Assert.Same(rows[0].CustomerName, Assert.Single(dynamic).Name);
    }
}
