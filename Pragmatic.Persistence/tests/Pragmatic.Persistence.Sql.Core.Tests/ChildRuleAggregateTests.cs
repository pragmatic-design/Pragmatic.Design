using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests.Live;

/// <summary>The Customers table with its orders seen through their visibility rule (<see cref="LiveOrder" />: a deleted order is not there).</summary>
[Entity]
[Table("Customers")]
public sealed class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    public List<LiveOrder> Orders { get; set; } = [];
}

/// <summary>A customer's count and total over its live orders, declared: the child's rule inside each correlated subquery.</summary>
[MapFrom<Customer>]
public sealed class CustomerLiveTotals
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public int OrdersCount { get; set; }

    [MapProperty("Orders.Sum(Total)")]
    public decimal OrdersTotal { get; set; }
}

/// <summary>
///     A declared DTO's aggregates over a collection whose child has visibility rules: the rules go inside the correlated subquery, their constants as
///     literals (a projection's column binds no value; a boolean as each dialect writes it), so the counts and sums see what the child's reads see.
///     Compiled and on the runtime path, compared with the seed.
/// </summary>
[Collection("databases")]
public class ChildRuleAggregateTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Aggregates_over_a_ruled_collection_count_the_visible_rows(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        var seed = databases.Seed;
        var customerId = seed.BusyCustomer.Id;
        var live = seed.Orders.Where(o => o.CustomerId == customerId && !o.IsDeleted).ToList();
        Assert.NotEqual(seed.Orders.Count(o => o.CustomerId == customerId), live.Count); // the busy customer has deleted orders: the rule matters
        using var session = databases.Session(dialect);

        var row = (await session.Query<Customer>().Where((c, id) => c.Id == id, customerId).Project<CustomerLiveTotals>().ToListAsync()).Single();
        Assert.Equal((live.Count, live.Sum(o => o.Total)), (row.OrdersCount, row.OrdersTotal));

        IQueryable<Customer> runtime = session.Query<Customer>();
        var composed = (await runtime.Where(c => c.Id == customerId).Project<Customer, CustomerLiveTotals>().ToListAsync()).Single();
        Assert.Equal((live.Count, live.Sum(o => o.Total)), (composed.OrdersCount, composed.OrdersTotal));
    }
}
