using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Subqueries on any chain, written inside a lambda: EXISTS and COUNT correlated to the outer row in a predicate, MIN and
///     SUM(CASE) as projected values (the cockpit's markers), IN (SELECT …) from a Select of one column, a declared chain as the
///     source with its value from the argument form. One statement, the subqueries under their own aliases.
/// </summary>
[Collection("databases")]
public class SubqueryTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void Subqueries_render_correlated_under_their_own_alias()
    {
        var session = Session(SqlDialect.PostgreSql);
        // EXISTS correlated to the outer row, with a constant; COUNT with a navigation inside the subquery.
        var withLines = session.Query<Order>().Where(o => session.Query<OrderLine>().Any(l => l.OrderId == o.Id && l.Quantity > 8));
        Assert.EndsWith("WHERE (EXISTS (SELECT 1 FROM \"OrderLines\" s1 WHERE ((s1.\"OrderId\" = t.\"Id\") AND (s1.\"Quantity\" > @p0))))", withLines.ToSql(), StringComparison.Ordinal);
        var busy = session.Query<Customer>().Where(c => session.Query<Order>().Where(o => o.CustomerId == c.Id).Count(o => o.Customer!.Country == c.Country) > 5);
        Assert.EndsWith("WHERE ((SELECT COUNT(*) FROM \"Orders\" s1 INNER JOIN \"Customers\" s1_Customer ON s1_Customer.\"Id\" = s1.\"CustomerId\" WHERE (s1.\"CustomerId\" = t.\"Id\") AND (s1_Customer.\"Country\" = t.\"Country\")) > @p0)", busy.ToSql(), StringComparison.Ordinal);

        // Scalars in a projection: MIN of a column, SUM of a CASE (constants inlined there), alongside a column of the row.
        var markers = session.Query<Customer>().Select(c => new
        {
            c.Name,
            Earliest = (DateTimeOffset?)session.Query<Order>().Where(o => o.CustomerId == c.Id && !o.IsDeleted).Min(o => o.CreatedAt),
            Weight = (int?)session.Query<Order>().Where(o => o.CustomerId == c.Id).Sum(o => o.Status == OrderStatus.Open ? 10 : o.Status == OrderStatus.Paid ? 1 : 0),
        });
        Assert.Equal("SELECT t.\"Name\", (SELECT MIN(s1.\"CreatedAt\") FROM \"Orders\" s1 WHERE ((s1.\"CustomerId\" = t.\"Id\") AND (NOT (s1.\"IsDeleted\" = true)))), (SELECT SUM(CASE WHEN s1.\"Status\" = 1 THEN 10 ELSE CASE WHEN s1.\"Status\" = 2 THEN 1 ELSE 0 END END) FROM \"Orders\" s1 WHERE (s1.\"CustomerId\" = t.\"Id\")) FROM \"Customers\" t", markers.ToSql());

        // IN (SELECT …) from a Select of one column; a declared chain as the source, its value from the argument form.
        var inSelect = Session(SqlDialect.SqlServer).Query<Customer>().Where((c, status) => Chains.WithStatus(session.Query<Order>(), status).Select(o => o.CustomerId).Contains(c.Id), OrderStatus.Cancelled);
        Assert.EndsWith("WHERE (t.[Id] IN (SELECT s1.[CustomerId] FROM [Orders] s1 WHERE ((s1.[Status] = @p0) AND (NOT (s1.[IsDeleted] = @p1)))))", inSelect.ToSql(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Subqueries_compute_what_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var byCustomer = seed.Orders.ToLookup(o => o.CustomerId);
        var linesByOrder = seed.Lines.ToLookup(l => l.OrderId);

        Assert.Equal(seed.Orders.Count(o => linesByOrder[o.Id].Any(l => l.Quantity > 8)), await session.Query<Order>().CountAsync(o => session.Query<OrderLine>().Any(l => l.OrderId == o.Id && l.Quantity > 8)));
        Assert.Equal(seed.Customers.Count(c => byCustomer[c.Id].Count() > 12), await session.Query<Customer>().CountAsync(c => session.Query<Order>().Count(o => o.CustomerId == c.Id) > 12));

        var markers = await session.Query<Customer>().OrderBy(c => c.Name).Take(20).Select(c => new
        {
            c.Name,
            Earliest = (DateTimeOffset?)session.Query<Order>().Where(o => o.CustomerId == c.Id && !o.IsDeleted).Min(o => o.CreatedAt),
            Weight = (int?)session.Query<Order>().Where(o => o.CustomerId == c.Id).Sum(o => o.Status == OrderStatus.Open ? 10 : o.Status == OrderStatus.Paid ? 1 : 0),
        }).ToListAsync();
        var expected = seed.Customers.OrderBy(c => c.Name, StringComparer.Ordinal).Take(20)
            .Select(c => (c.Name, byCustomer[c.Id].Where(o => !o.IsDeleted).Select(o => (DateTimeOffset?)o.CreatedAt).Min(), byCustomer[c.Id].Any() ? (int?)byCustomer[c.Id].Sum(o => o.Status == OrderStatus.Open ? 10 : o.Status == OrderStatus.Paid ? 1 : 0) : null));
        Assert.Equal(expected, markers.Select(m => (m.Name, m.Earliest, m.Weight)));

        var cancelled = seed.Orders.Where(o => o.Status == OrderStatus.Cancelled && !o.IsDeleted).Select(o => o.CustomerId).ToHashSet();
        Assert.Equal(seed.Customers.Count(c => cancelled.Contains(c.Id)), await session.Query<Customer>().CountAsync((c, status) => Chains.WithStatus(session.Query<Order>(), status).Select(o => o.CustomerId).Contains(c.Id), OrderStatus.Cancelled));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Nested_subqueries_read_the_rows_of_every_level_above(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var byCustomer = seed.Orders.ToLookup(o => o.CustomerId);
        var linesByOrder = seed.Lines.ToLookup(l => l.OrderId);

        // Depth 1 on a collection navigation, reading its parent: the orders placed after the customer's own creation.
        Assert.Equal(seed.Customers.Count(c => byCustomer[c.Id].Any(o => o.CreatedAt > c.CreatedAt)),
            await session.Query<Customer>().CountAsync(c => c.Orders.Any(o => o.CreatedAt > c.CreatedAt)));

        // Depth 2, reading the parent and the grandparent.
        Assert.Equal(seed.Customers.Count(c => byCustomer[c.Id].Any(o => linesByOrder[o.Id].Any(l => l.Quantity > 8 && o.CreatedAt > c.CreatedAt))),
            await session.Query<Customer>().CountAsync(c => c.Orders.Any(o => o.Lines.Any(l => l.Quantity > 8 && o.CreatedAt > c.CreatedAt))));

        // Depth 3, navigations and written chains mixed: the innermost reads the order two levels up and the customer three.
        Assert.Equal(seed.Customers.Count(c => byCustomer[c.Id].Any(o => linesByOrder[o.Id].Any() && byCustomer[c.Id].Count(again => again.Status == o.Status) >= 2)),
            await session.Query<Customer>().CountAsync(c => c.Orders.Any(o => session.Query<OrderLine>().Any(l => l.OrderId == o.Id
                && session.Query<Order>().Count(again => again.CustomerId == c.Id && again.Status == o.Status) >= 2))));
    }
}
