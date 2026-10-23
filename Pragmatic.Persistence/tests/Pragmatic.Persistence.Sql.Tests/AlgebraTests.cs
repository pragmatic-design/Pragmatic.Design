using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     The algebra beyond one hop and one table: navigation paths of any depth (chained joins), collection predicates as
///     correlated subqueries (EXISTS, NOT EXISTS, COUNT), CASE from <c>?:</c> and COALESCE from <c>??</c>; the same on the runtime path.
/// </summary>
[Collection("databases")]
public class AlgebraTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void Paths_collections_and_conditionals_translate_to_chained_joins_subqueries_and_case()
    {
        // Two hops: the second join hangs off the first's alias, as an Include path does.
        var twoHops = Session(SqlDialect.PostgreSql).Query<OrderLine>().Where(l => l.Order!.Customer!.Country == "IT").Select(l => new { l.Sku, Who = l.Order!.Customer!.Name });
        Assert.Equal("SELECT t.\"Sku\", j_Order_Customer.\"Name\" FROM \"OrderLines\" t INNER JOIN \"Orders\" j_Order ON j_Order.\"Id\" = t.\"OrderId\" INNER JOIN \"Customers\" j_Order_Customer ON j_Order_Customer.\"Id\" = j_Order.\"CustomerId\" WHERE (j_Order_Customer.\"Country\" = @p0)", twoHops.ToSql());

        // Collection predicates: EXISTS with the inner predicate under the child's alias, a correlated COUNT, NOT EXISTS for All.
        var any = Session(SqlDialect.Sqlite).Query<Order>().Where(o => o.Lines.Any(l => l.Quantity > 5));
        Assert.EndsWith("WHERE (EXISTS (SELECT 1 FROM \"OrderLines\" c1 WHERE c1.\"OrderId\" = t.\"Id\" AND (c1.\"Quantity\" > @p0)))", any.ToSql(), StringComparison.Ordinal);
        var count = Session(SqlDialect.SqlServer).Query<Order>().Where(o => o.Lines.Count > 2 && o.Lines.Count(l => l.Sku == "BOLT") == 1);
        Assert.Contains("((SELECT COUNT(*) FROM [OrderLines] c1 WHERE c1.[OrderId] = t.[Id]) > @p0) AND ((SELECT COUNT(*) FROM [OrderLines] c1 WHERE c1.[OrderId] = t.[Id] AND (c1.[Sku] = @p1)) = @p2)", count.ToSql(), StringComparison.Ordinal);
        var all = Session(SqlDialect.PostgreSql).Query<Order>().Where(o => o.Lines.All(l => l.Quantity >= 1));
        Assert.EndsWith("WHERE (NOT EXISTS (SELECT 1 FROM \"OrderLines\" c1 WHERE c1.\"OrderId\" = t.\"Id\" AND NOT (c1.\"Quantity\" >= @p0)))", all.ToSql(), StringComparison.Ordinal);
        // A navigation inside the inner predicate: a join inside the subquery, under the child's alias.
        var innerJoin = Session(SqlDialect.Sqlite).Query<Customer>().Where(c => c.Orders.Any(o => o.Customer!.Country == "IT" && !o.IsDeleted));
        Assert.Contains("EXISTS (SELECT 1 FROM \"Orders\" c1 INNER JOIN \"Customers\" c1_Customer ON c1_Customer.\"Id\" = c1.\"CustomerId\" WHERE c1.\"CustomerId\" = t.\"Id\" AND (", innerJoin.ToSql(), StringComparison.Ordinal);

        // CASE and COALESCE as values, in a projection (constants inlined) and in a predicate (constants as parameters).
        var labelled = Session(SqlDialect.PostgreSql).Query<Order>().Select(o => new { Label = o.IsDeleted ? "gone" : "live", Who = o.Customer!.Name ?? "none" });
        Assert.StartsWith("SELECT CASE WHEN t.\"IsDeleted\" = true THEN 'gone' ELSE 'live' END, COALESCE(j_Customer.\"Name\", 'none') FROM", labelled.ToSql(), StringComparison.Ordinal);
        Assert.Contains("CASE WHEN t.\"IsDeleted\" = 1 THEN", Session(SqlDialect.Sqlite).Query<Order>().Select(o => new { Label = o.IsDeleted ? "gone" : "live" }).ToSql(), StringComparison.Ordinal); // the literal true of each dialect
        var branched = Session(SqlDialect.Sqlite).Query<Order>().Where(o => (o.Status == OrderStatus.Open ? o.Total : 0m) > 100m);
        Assert.EndsWith("WHERE (CASE WHEN t.\"Status\" = @p0 THEN t.\"Total\" ELSE @p1 END > @p2)", branched.ToSql(), StringComparison.Ordinal);

        // The runtime path renders the same statements.
        var session = Session(SqlDialect.PostgreSql);
        IQueryable<OrderLine> lines = session.Query<OrderLine>();
        Assert.Equal(session.Query<OrderLine>().Where(l => l.Order!.Customer!.Country == "IT").Select(l => l.Sku).ToSql(), lines.Where(l => l.Order!.Customer!.Country == "IT").Select(l => l.Sku).ToSql());
        IQueryable<Order> orders = session.Query<Order>();
        Assert.Equal(session.Query<Order>().Where(o => o.Lines.Any(l => l.Quantity > 5) && o.Lines.Count > 2).ToSql(), orders.Where(o => o.Lines.Any(l => l.Quantity > 5) && o.Lines.Count > 2).ToSql());
        Assert.Equal(session.Query<Order>().Where(o => (o.Status == OrderStatus.Open ? o.Total : 0m) > 100m).ToSql(), orders.Where(o => (o.Status == OrderStatus.Open ? o.Total : 0m) > 100m).ToSql());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Paths_collections_and_conditionals_compute_what_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var customers = seed.Customers.ToDictionary(c => c.Id);
        var linesByOrder = seed.Lines.GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        // Two hops from a line to its order's customer.
        Assert.Equal(seed.Lines.Count(l => customers[seed.Orders.First(o => o.Id == l.OrderId).CustomerId].Country == "IT"), await session.Query<OrderLine>().CountAsync(l => l.Order!.Customer!.Country == "IT"));

        // Collection predicates against the seed's lines.
        Assert.Equal(seed.Orders.Count(o => linesByOrder[o.Id].Any(l => l.Quantity > 8)), await session.Query<Order>().CountAsync(o => o.Lines.Any(l => l.Quantity > 8)));
        Assert.Equal(seed.Orders.Count(o => linesByOrder[o.Id].All(l => l.Quantity >= 3)), await session.Query<Order>().CountAsync(o => o.Lines.All(l => l.Quantity >= 3)));
        Assert.Equal(seed.Orders.Count(o => linesByOrder[o.Id].Count(l => l.Sku == "BOLT") == 2), await session.Query<Order>().CountAsync(o => o.Lines.Count(l => l.Sku == "BOLT") == 2));
        Assert.Equal(seed.Orders.Count, await session.Query<Order>().CountAsync(o => o.Lines.Count == Seed.LinesPerOrder));
        Assert.Equal(seed.Customers.Count(c => seed.Orders.Any(o => o.CustomerId == c.Id && o.Status == OrderStatus.Cancelled)), await session.Query<Customer>().CountAsync(c => c.Orders.Any(o => o.Status == OrderStatus.Cancelled)));

        // CASE and COALESCE.
        var labels = await session.Query<Order>().OrderBy(o => o.Number).Take(5).Select(o => new { o.Number, Label = o.IsDeleted ? "gone" : "live", Who = o.Customer!.Name ?? "none" }).ToListAsync();
        Assert.Equal(seed.Orders.OrderBy(o => o.Number, StringComparer.Ordinal).Take(5).Select(o => (o.Number, o.IsDeleted ? "gone" : "live", customers[o.CustomerId].Name)), labels.Select(l => (l.Number, l.Label, l.Who)));
        Assert.Equal(seed.Orders.Count(o => (o.Status == OrderStatus.Open ? o.Total : 0m) > 1000m), await session.Query<Order>().CountAsync(o => (o.Status == OrderStatus.Open ? o.Total : 0m) > 1000m));

        // The runtime path, twice for the shape cache.
        IQueryable<Order> composed = session.Query<Order>();
        Assert.Equal(seed.Orders.Count(o => linesByOrder[o.Id].Any(l => l.Quantity > 8)), await composed.Where(o => o.Lines.Any(l => l.Quantity > 8)).CountAsync());
        Assert.Equal(seed.Orders.Count(o => linesByOrder[o.Id].Any(l => l.Quantity > 7)), await composed.Where(o => o.Lines.Any(l => l.Quantity > 7)).CountAsync());
        IQueryable<OrderLine> lines = session.Query<OrderLine>();
        Assert.Equal(seed.Lines.Count(l => customers[seed.Orders.First(o => o.Id == l.OrderId).CustomerId].Country == "DE"), await lines.Where(l => l.Order!.Customer!.Country == "DE").CountAsync());
    }
}
