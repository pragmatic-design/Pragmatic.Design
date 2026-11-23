using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     RIGHT and FULL OUTER JOIN, the operators EF Core translates from <c>Queryable.RightJoin</c> (.NET 10) and <c>Queryable.FullJoin</c> (.NET 11).
///     A side that may be missing keeps its own filters and rules: in the ON clause, so its rows that fail them match nothing, and, where that side's
///     rows are kept on their own, in the WHERE too, guarded by its key, so a row of the other side alone is not dropped by them.
/// </summary>
[Collection("databases")]
public class OuterJoinTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    public sealed class NameNumberDto
    {
        public string? Name { get; set; }
        public string? Number { get; set; }
        public decimal? Total { get; set; }
    }

    [Fact]
    public void Outer_joins_keep_each_side_filtered_by_its_own_predicates()
    {
        var session = Session(SqlDialect.PostgreSql);

        // RIGHT JOIN: the outer chain's filter in the ON clause (an Italian customer or none), the inner chain's in the WHERE (every open order).
        var right = session.Query<Customer>().Where(c => c.Country == "IT")
            .RightJoin(session.Query<Order>().Where(o => o.Status == OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Number });
        Assert.Equal("SELECT t.\"Name\", j1.\"Number\" FROM \"Customers\" t RIGHT JOIN \"Orders\" j1 ON j1.\"CustomerId\" = t.\"Id\" AND (t.\"Country\" = @p0) WHERE (j1.\"Status\" = @p1)", right.ToSql());

        // FULL JOIN: each side's filter in the ON clause, and in the WHERE unless the row is the other side's alone.
        var full = session.Query<Customer>().Where(c => c.Country == "IT")
            .FullJoin(session.Query<Order>().Where(o => o.Status == OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Number });
        Assert.Equal(
            "SELECT t.\"Name\", j1.\"Number\" FROM \"Customers\" t FULL JOIN \"Orders\" j1 ON j1.\"CustomerId\" = t.\"Id\" AND (j1.\"Status\" = @p1) AND (t.\"Country\" = @p0)"
            + " WHERE (t.\"Id\" IS NULL OR (t.\"Country\" = @p0)) AND (j1.\"Id\" IS NULL OR (j1.\"Status\" = @p1))", full.ToSql());

        // Unfiltered: nothing to guard.
        var plain = session.Query<Customer>().FullJoin(session.Query<Order>(), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Number });
        Assert.Equal("SELECT t.\"Name\", j1.\"Number\" FROM \"Customers\" t FULL JOIN \"Orders\" j1 ON j1.\"CustomerId\" = t.\"Id\"", plain.ToSql());

        // MySQL has no FULL JOIN: the LEFT JOIN's rows, then the RIGHT JOIN's without a customer, the ordering's value carried out of the union.
        var mysql = Session(SqlDialect.MySql);
        var emulated = mysql.Query<Customer>().OrderBy(c => c.Name).FullJoin(mysql.Query<Order>(), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Number }).Take(5);
        Assert.Equal(
            "SELECT fj.c0, fj.c1 FROM (SELECT t.\"Name\" AS c0, j1.\"Number\" AS c1, t.\"Name\" AS o0 FROM \"Customers\" t LEFT JOIN \"Orders\" j1 ON j1.\"CustomerId\" = t.\"Id\""
            + " UNION ALL SELECT t.\"Name\" AS c0, j1.\"Number\" AS c1, t.\"Name\" AS o0 FROM \"Customers\" t RIGHT JOIN \"Orders\" j1 ON j1.\"CustomerId\" = t.\"Id\" WHERE (t.\"Id\" IS NULL)) fj ORDER BY fj.o0 LIMIT @p0",
            emulated.ToSql());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Outer_joins_return_the_rows_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        // The first ten days' open orders: about a hundred, so some Italian customers have none and most of the orders are another country's.
        var before = new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero);
        var italians = seed.Customers.Where(c => c.Country == "IT").ToList();
        var open = seed.Orders.Where(o => o.Status == OrderStatus.Open && o.CreatedAt < before).ToList();
        Assert.Contains(italians, c => open.All(o => o.CustomerId != c.Id)); // the seed has rows unmatched on either side, or the test proves nothing
        Assert.Contains(open, o => italians.All(c => c.Id != o.CustomerId));

        // RIGHT JOIN: every one of those orders, with its customer's name when the customer is Italian.
        var expectedRight = open.Select(o => (italians.FirstOrDefault(c => c.Id == o.CustomerId)?.Name, (string?)o.Number)).OrderBy(r => r.Item2, StringComparer.Ordinal).ToList();
        var right = await session.Query<Customer>().Where(c => c.Country == "IT")
            .RightJoin(session.Query<Order>().Where((o, b) => o.Status == OrderStatus.Open && o.CreatedAt < b, before), c => c.Id, o => o.CustomerId, (c, o) => new NameNumberDto { Name = c.Name, Number = o.Number })
            .ToListAsync();
        Assert.Equal(expectedRight, right.Select(r => (r.Name, r.Number)).OrderBy(r => r.Number, StringComparer.Ordinal));

        // FULL JOIN: the pairs, the Italian customers without an open order, the open orders of no Italian customer.
        var pairs = italians.SelectMany(c => open.Where(o => o.CustomerId == c.Id).Select(o => ((string?)c.Name, (string?)o.Number, (decimal?)o.Total)));
        var customersAlone = italians.Where(c => open.All(o => o.CustomerId != c.Id)).Select(c => ((string?)c.Name, (string?)null, (decimal?)null));
        var ordersAlone = open.Where(o => italians.All(c => c.Id != o.CustomerId)).Select(o => ((string?)null, (string?)o.Number, (decimal?)o.Total));
        var expectedFull = pairs.Concat(customersAlone).Concat(ordersAlone).OrderBy(r => r.Item1, StringComparer.Ordinal).ThenBy(r => r.Item2, StringComparer.Ordinal).ToList();
        var full = await session.Query<Customer>().Where(c => c.Country == "IT")
            .FullJoin(session.Query<Order>().Where((o, b) => o.Status == OrderStatus.Open && o.CreatedAt < b, before), c => c.Id, o => o.CustomerId, (c, o) => new NameNumberDto { Name = c.Name, Number = o.Number, Total = (decimal?)o.Total })
            .ToListAsync();
        Assert.Equal(expectedFull, full.Select(r => (r.Name, r.Number, r.Total)).OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Number, StringComparer.Ordinal));
        Assert.Equal(expectedFull.Count, await session.Query<Customer>().Where(c => c.Country == "IT")
            .FullJoin(session.Query<Order>().Where((o, b) => o.Status == OrderStatus.Open && o.CreatedAt < b, before), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Number }).CountAsync());

        // Ordered by the outer row's value and paged: where the rows without a customer go is the dialect's (NULLS FIRST or LAST), the customers' order is not.
        var ordered = await session.Query<Customer>().Where(c => c.Country == "IT").OrderBy(c => c.Name)
            .FullJoin(session.Query<Order>().Where((o, b) => o.Status == OrderStatus.Open && o.CreatedAt < b, before), c => c.Id, o => o.CustomerId, (c, o) => new NameNumberDto { Name = c.Name, Number = o.Number })
            .ToListAsync();
        Assert.Equal(expectedFull.Count, ordered.Count);
        var names = ordered.Where(r => r.Name is not null).Select(r => r.Name!).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
        Assert.Equal(5, (await session.Query<Customer>().Where(c => c.Country == "IT").OrderBy(c => c.Name)
            .FullJoin(session.Query<Order>().Where((o, b) => o.Status == OrderStatus.Open && o.CreatedAt < b, before), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Number })
            .Take(5).ToListAsync()).Count);
    }
}
