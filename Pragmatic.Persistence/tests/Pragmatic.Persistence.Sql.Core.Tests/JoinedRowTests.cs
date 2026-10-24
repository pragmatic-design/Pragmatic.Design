using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     The row after a join: a composite of the entities and values the result selector named, never materialised between
///     operators. More joins, Where, OrderBy, GroupBy, Select and the terminals work on it; the reader builds the anonymous
///     row (entities hydrated, a LEFT JOIN's as null) only at the end.
/// </summary>
[Collection("databases")]
public class JoinedRowTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void Operators_after_a_join_work_on_the_joined_row()
    {
        // Two joins in a chain, a Where on both rows after them, an ordering, a projection: one statement, every member a column of it.
        var chain = Session(SqlDialect.PostgreSql).Query<Order>()
            .Join(Session(SqlDialect.PostgreSql).Query<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => new { Order = o, Customer = c })
            .Join(Session(SqlDialect.PostgreSql).Query<OrderLine>(), x => x.Order.Id, l => l.OrderId, (x, l) => new { x.Order, x.Customer, Line = l })
            .Where(x => x.Customer.Country == "IT" && x.Line.Quantity > 5 && !x.Order.IsDeleted)
            .OrderBy(x => x.Order.Number).ThenBy(x => x.Line.Sku)
            .Select(x => new { x.Order.Number, x.Customer.Name, x.Line.Sku, x.Line.Quantity });
        Assert.Equal("SELECT t.\"Number\", j1.\"Name\", j2.\"Sku\", j2.\"Quantity\" FROM \"Orders\" t INNER JOIN \"Customers\" j1 ON j1.\"Id\" = t.\"CustomerId\" INNER JOIN \"OrderLines\" j2 ON j2.\"OrderId\" = t.\"Id\" WHERE (((j1.\"Country\" = @p0) AND (j2.\"Quantity\" > @p1)) AND (NOT (t.\"IsDeleted\" = @p2))) ORDER BY t.\"Number\", j2.\"Sku\"", chain.ToSql());

        // No Select: the row itself is read, the entities hydrated from their columns; a LEFT JOIN's entity is null when unmatched.
        var rows = Session(SqlDialect.Sqlite).Query<Customer>()
            .LeftJoin(Session(SqlDialect.Sqlite).Query<Order>().Where(o => o.Status == OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, o) => new { Customer = c, Open = o })
            .Where(x => x.Customer.Country == "DE");
        Assert.Equal("SELECT t.\"Id\", t.\"Name\", t.\"Email\", t.\"Country\", t.\"CreatedAt\", j1.\"Id\", j1.\"CustomerId\", j1.\"Number\", j1.\"Status\", j1.\"Total\", j1.\"CreatedAt\", j1.\"IsDeleted\" FROM \"Customers\" t LEFT JOIN \"Orders\" j1 ON j1.\"CustomerId\" = t.\"Id\" AND (j1.\"Status\" = @p0) WHERE (t.\"Country\" = @p1)", rows.ToSql());

        // GroupBy on the joined row, aggregates over its members; a navigation from a joined entity in the key.
        var grouped = Session(SqlDialect.SqlServer).Query<OrderLine>()
            .Join(Session(SqlDialect.SqlServer).Query<Order>(), l => l.OrderId, o => o.Id, (l, o) => new { Line = l, Order = o })
            .GroupBy(x => x.Order.Customer!.Country)
            .Select(g => new { Country = g.Key, Units = g.Sum(x => x.Line.Quantity), Orders = g.Count() });
        Assert.Equal("SELECT j1_Customer.[Country], SUM(t.[Quantity]), COUNT(*) FROM [OrderLines] t INNER JOIN [Orders] j1 ON j1.[Id] = t.[OrderId] INNER JOIN [Customers] j1_Customer ON j1_Customer.[Id] = j1.[CustomerId] GROUP BY j1_Customer.[Country]", grouped.ToSql());

        // A value rendered in the selector is carried forward as a column; a navigation member becomes an entity row under its join.
        var carried = Session(SqlDialect.PostgreSql).Query<OrderLine>()
            .Join(Session(SqlDialect.PostgreSql).Query<Order>(), l => l.OrderId, o => o.Id, (l, o) => new { Who = o.Customer, Amount = l.Quantity * 2, l.Sku })
            .Where(x => x.Who!.Country == "IT" && x.Amount > 10)
            .Select(x => new { x.Who!.Name, x.Amount });
        Assert.Equal("SELECT j1_Customer.\"Name\", t.\"Quantity\" * 2 FROM \"OrderLines\" t INNER JOIN \"Orders\" j1 ON j1.\"Id\" = t.\"OrderId\" INNER JOIN \"Customers\" j1_Customer ON j1_Customer.\"Id\" = j1.\"CustomerId\" WHERE ((j1_Customer.\"Country\" = @p0) AND (t.\"Quantity\" * 2 > @p1))", carried.ToSql());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Joined_rows_compute_what_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var customers = seed.Customers.ToDictionary(c => c.Id);
        var orders = seed.Orders.ToDictionary(o => o.Id);

        // Two joins, filters on both, projection: the lines of Italian customers with more than 8 units.
        var expected = seed.Lines.Where(l => l.Quantity > 8 && customers[orders[l.OrderId].CustomerId].Country == "IT" && !orders[l.OrderId].IsDeleted)
            .Select(l => (orders[l.OrderId].Number, customers[orders[l.OrderId].CustomerId].Name, l.Sku, l.Quantity)).OrderBy(r => r.Number, StringComparer.Ordinal).ThenBy(r => r.Sku, StringComparer.Ordinal).ToList();
        var rows = await session.Query<Order>()
            .Join(session.Query<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => new { Order = o, Customer = c })
            .Join(session.Query<OrderLine>(), x => x.Order.Id, l => l.OrderId, (x, l) => new { x.Order, x.Customer, Line = l })
            .Where(x => x.Customer.Country == "IT" && x.Line.Quantity > 8 && !x.Order.IsDeleted)
            .OrderBy(x => x.Order.Number).ThenBy(x => x.Line.Sku)
            .Select(x => new { x.Order.Number, x.Customer.Name, x.Line.Sku, x.Line.Quantity }).ToListAsync();
        Assert.Equal(expected, rows.Select(r => (r.Number, r.Name, r.Sku, r.Quantity)));

        // The row read as it is: entities hydrated; the LEFT JOIN's order null for customers without an open order.
        var hydrated = await session.Query<Customer>()
            .LeftJoin(session.Query<Order>().Where(o => o.Status == OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, o) => new { Customer = c, Open = o })
            .Where(x => x.Customer.Country == "DE").ToListAsync();
        var germans = seed.Customers.Where(c => c.Country == "DE").ToList();
        var openByCustomer = seed.Orders.Where(o => o.Status == OrderStatus.Open).ToLookup(o => o.CustomerId);
        Assert.Equal(germans.Sum(c => Math.Max(1, openByCustomer[c.Id].Count())), hydrated.Count);
        Assert.All(hydrated, x => Assert.Equal(customers[x.Customer.Id].Name, x.Customer.Name));
        Assert.Equal(germans.Count(c => !openByCustomer[c.Id].Any()), hydrated.Count(x => x.Open is null));
        Assert.All(hydrated.Where(x => x.Open is not null), x => Assert.Equal(OrderStatus.Open, x.Open!.Status));

        // GroupBy on the joined row.
        var units = await session.Query<OrderLine>()
            .Join(session.Query<Order>(), l => l.OrderId, o => o.Id, (l, o) => new { Line = l, Order = o })
            .GroupBy(x => x.Order.Customer!.Country)
            .Select(g => new { Country = g.Key, Units = g.Sum(x => x.Line.Quantity), Lines = g.Count() }).ToListAsync();
        var expectedUnits = seed.Lines.GroupBy(l => customers[orders[l.OrderId].CustomerId].Country).Select(g => (g.Key, g.Sum(l => l.Quantity), g.Count())).OrderBy(g => g.Key, StringComparer.Ordinal);
        Assert.Equal(expectedUnits, units.Select(u => (u.Country, u.Units, u.Lines)).OrderBy(u => u.Item1, StringComparer.Ordinal));

        // Six joins in a chain (self-joins on the same keys): the shape grows, the statement stays one; a count after a Where on the sixth row.
        var six = await session.Query<Order>()
            .Join(session.Query<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => new { Order = o, Customer = c })
            .Join(session.Query<Order>(), x => x.Order.Id, o2 => o2.Id, (x, o2) => new { x.Order, x.Customer, Same = o2 })
            .Join(session.Query<Customer>(), x => x.Same.CustomerId, c2 => c2.Id, (x, c2) => new { x.Order, x.Customer, x.Same, Again = c2 })
            .LeftJoin(session.Query<OrderLine>().Where(l => l.Sku == "BOLT"), x => x.Order.Id, l => l.OrderId, (x, l) => new { x.Order, x.Customer, x.Same, x.Again, Bolt = l })
            .Join(session.Query<Order>(), x => x.Same.Id, o3 => o3.Id, (x, o3) => new { x.Order, x.Customer, x.Same, x.Again, x.Bolt, Third = o3 })
            .Join(session.Query<Customer>(), x => x.Third.CustomerId, c3 => c3.Id, (x, c3) => new { x.Order, x.Customer, x.Same, x.Again, x.Bolt, x.Third, Last = c3 })
            .Where(x => x.Last.Country == "IT" && x.Again.Country == "IT" && x.Bolt != null && x.Third.Status == OrderStatus.Open)
            .CountAsync();
        var expectedSix = seed.Lines.Count(l => l.Sku == "BOLT" && orders[l.OrderId].Status == OrderStatus.Open && customers[orders[l.OrderId].CustomerId].Country == "IT");
        Assert.Equal(expectedSix, six);
    }
}
