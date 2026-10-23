using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     SelectMany: a row per element of a collection of the row. LINQ's GroupJoin + SelectMany(DefaultIfEmpty) as the LEFT JOIN it
///     is; a collection navigation as a JOIN on the foreign key; a chain correlated to the row with OrderBy and Take ("the last N
///     per parent"): LATERAL on PostgreSQL, APPLY on SQL Server, a derived table with ROW_NUMBER on SQLite.
/// </summary>
[Collection("databases")]
public class SelectManyTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void SelectMany_renders_joins_and_lateral_per_dialect()
    {
        // GroupJoin + SelectMany(DefaultIfEmpty): the LEFT JOIN LINQ spells this way; without DefaultIfEmpty, INNER.
        var leftJoin = Session(SqlDialect.PostgreSql).Query<Customer>()
            .GroupJoin(Session(SqlDialect.PostgreSql).Query<Order>().Where(o => o.Status == OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, orders) => new { c, orders })
            .SelectMany(x => x.orders.DefaultIfEmpty(), (x, o) => new { x.c.Name, Number = o!.Number });
        Assert.Equal("SELECT t.\"Name\", j1.\"Number\" FROM \"Customers\" t LEFT JOIN \"Orders\" j1 ON j1.\"CustomerId\" = t.\"Id\" AND (j1.\"Status\" = @p0)", leftJoin.ToSql());
        var innerJoin = Session(SqlDialect.Sqlite).Query<Customer>()
            .GroupJoin(Session(SqlDialect.Sqlite).Query<Order>(), c => c.Id, o => o.CustomerId, (c, orders) => new { c, orders })
            .SelectMany(x => x.orders, (x, o) => new { x.c.Name, o.Number });
        Assert.Equal("SELECT t.\"Name\", j1.\"Number\" FROM \"Customers\" t INNER JOIN \"Orders\" j1 ON j1.\"CustomerId\" = t.\"Id\"", innerJoin.ToSql());

        // A collection navigation: a JOIN on the foreign key; from a joined row's member too.
        var lines = Session(SqlDialect.SqlServer).Query<Order>().Where(o => !o.IsDeleted).SelectMany(o => o.Lines, (o, l) => new { o.Number, l.Sku, l.Quantity }).Where(x => x.Quantity > 8);
        Assert.Equal("SELECT t.[Number], j1.[Sku], j1.[Quantity] FROM [Orders] t INNER JOIN [OrderLines] j1 ON j1.[OrderId] = t.[Id] WHERE (NOT (t.[IsDeleted] = @p0)) AND (j1.[Quantity] > @p1)", lines.ToSql());

        // The last two open orders per customer: a correlated chain with OrderBy and Take.
        var session = Session(SqlDialect.PostgreSql);
        var lastTwo = session.Query<Customer>().SelectMany(c => session.Query<Order>().Where(o => o.CustomerId == c.Id && o.Status == OrderStatus.Open).OrderByDescending(o => o.CreatedAt).Take(2), (c, o) => new { c.Name, o.Number });
        Assert.Equal("SELECT t.\"Name\", j1.c2 FROM \"Customers\" t CROSS JOIN LATERAL (SELECT l1.\"Id\" AS c0, l1.\"CustomerId\" AS c1, l1.\"Number\" AS c2, l1.\"Status\" AS c3, l1.\"Total\" AS c4, l1.\"CreatedAt\" AS c5, l1.\"IsDeleted\" AS c6 FROM \"Orders\" l1 WHERE (l1.\"CustomerId\" = t.\"Id\") AND (l1.\"Status\" = @p0) ORDER BY l1.\"CreatedAt\" DESC LIMIT 2) j1", lastTwo.ToSql());
        var sqlServer = Session(SqlDialect.SqlServer);
        var applied = sqlServer.Query<Customer>().SelectMany(c => sqlServer.Query<Order>().Where(o => o.CustomerId == c.Id).OrderByDescending(o => o.CreatedAt).Take(2).DefaultIfEmpty(), (c, o) => new { c.Name, Number = o!.Number });
        Assert.StartsWith("SELECT t.[Name], j1.c2 FROM [Customers] t OUTER APPLY (SELECT l1.[Id] AS c0", applied.ToSql(), StringComparison.Ordinal);
        Assert.EndsWith("FROM [Orders] l1 WHERE (l1.[CustomerId] = t.[Id]) ORDER BY l1.[CreatedAt] DESC OFFSET 0 ROWS FETCH NEXT 2 ROWS ONLY) j1", applied.ToSql(), StringComparison.Ordinal);
        var sqlite = Session(SqlDialect.Sqlite);
        var emulated = sqlite.Query<Customer>().SelectMany(c => sqlite.Query<Order>().Where(o => o.CustomerId == c.Id && o.Status == OrderStatus.Open).OrderByDescending(o => o.CreatedAt).Take(2), (c, o) => new { c.Name, o.Number });
        Assert.EndsWith(", ROW_NUMBER() OVER (PARTITION BY l1.\"CustomerId\" ORDER BY l1.\"CreatedAt\" DESC) AS rn FROM \"Orders\" l1 WHERE (l1.\"Status\" = @p0)) j1 ON j1.c1 = t.\"Id\" AND j1.rn <= 2", emulated.ToSql(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task SelectMany_computes_what_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var byCustomer = seed.Orders.ToLookup(o => o.CustomerId);
        var orders = seed.Orders.ToDictionary(o => o.Id);

        // GroupJoin + DefaultIfEmpty: every customer, with each open order or once with null.
        var open = seed.Orders.Where(o => o.Status == OrderStatus.Open).ToLookup(o => o.CustomerId);
        var leftJoined = await session.Query<Customer>()
            .GroupJoin(session.Query<Order>().Where(o => o.Status == OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, orders) => new { c, orders })
            .SelectMany(x => x.orders.DefaultIfEmpty(), (x, o) => new { x.c.Name, Number = o!.Number }).ToListAsync();
        Assert.Equal(seed.Customers.Sum(c => Math.Max(1, open[c.Id].Count())), leftJoined.Count);
        Assert.Equal(seed.Customers.Count(c => !open[c.Id].Any()), leftJoined.Count(r => r.Number is null));

        // A collection navigation: the lines of the undeleted orders above eight units.
        var heavy = await session.Query<Order>().Where(o => !o.IsDeleted).SelectMany(o => o.Lines, (o, l) => new { o.Number, l.Sku, l.Quantity }).Where(x => x.Quantity > 8).CountAsync();
        Assert.Equal(seed.Lines.Count(l => l.Quantity > 8 && !orders[l.OrderId].IsDeleted), heavy);

        // The last two open orders per customer, on every dialect (LATERAL, APPLY, ROW_NUMBER).
        var lastTwo = await session.Query<Customer>()
            .SelectMany(c => session.Query<Order>().Where(o => o.CustomerId == c.Id && o.Status == OrderStatus.Open).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Take(2), (c, o) => new { c.Name, o.Number })
            .OrderBy(x => x.Name).ThenBy(x => x.Number).ToListAsync();
        var expected = seed.Customers.SelectMany(c => open[c.Id].OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Take(2).Select(o => (c.Name, o.Number))).OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, lastTwo.Select(r => (r.Name, r.Number)).OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Number, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_correlated_chain_may_compare_the_rows_without_an_equality(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);

        // Each customer's two latest orders placed after the customer's own sign-up instant: the predicate reads both rows, not by equality.
        var rows = await session.Query<Customer>()
            .SelectMany(c => session.Query<Order>().Where(o => o.CustomerId == c.Id && o.CreatedAt > c.CreatedAt).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Take(2), (c, o) => new { c.Name, o.Number })
            .ToListAsync();
        var expected = seed.Customers.SelectMany(c => seed.Orders.Where(o => o.CustomerId == c.Id && o.CreatedAt > c.CreatedAt).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Take(2).Select(o => (c.Name, o.Number)))
            .OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();
        Assert.True(expected.Count > 10, $"{expected.Count} rows: too few to tell");
        Assert.Equal(expected, rows.Select(r => (r.Name, r.Number)).OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Number, StringComparer.Ordinal));
    }
}
