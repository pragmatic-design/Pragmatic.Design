using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     The aggregates of a group beyond the five plain ones: filtered (<c>g.Where(pred).Sum(…)</c>, <c>g.Count(pred)</c>: a CASE inside,
///     the form every dialect has), distinct (<c>g.Select(col).Distinct().Count()</c>), text (<c>string.Join(sep, g.Select(col))</c>:
///     STRING_AGG, group_concat on SQLite), in the projection, the HAVING and the ordering; a HAVING on the key; all on a joined row too.
/// </summary>
[Collection("databases")]
public class GroupAggregateTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void Extended_aggregates_render_on_every_dialect()
    {
        var report = Session(SqlDialect.PostgreSql).Query<Order>().GroupBy(o => o.CustomerId)
            .Where(g => g.Count(o => o.Status == OrderStatus.Open) > 2)
            .OrderByDescending(g => g.Where(o => !o.IsDeleted).Sum(o => o.Total))
            .Select(g => new
            {
                Customer = g.Key,
                Open = g.Count(o => o.Status == OrderStatus.Open),
                Live = (decimal?)g.Where(o => !o.IsDeleted).Sum(o => o.Total),
                Statuses = g.Select(o => o.Status).Distinct().Count(),
                Numbers = string.Join(", ", g.Where(o => o.Status == OrderStatus.Paid).Select(o => o.Number)),
            });
        Assert.Equal("SELECT t.\"CustomerId\", COUNT(CASE WHEN t.\"Status\" = 1 THEN 1 END), SUM(CASE WHEN NOT (t.\"IsDeleted\" = true) THEN t.\"Total\" END), COUNT(DISTINCT t.\"Status\"), STRING_AGG(CASE WHEN t.\"Status\" = 2 THEN t.\"Number\" END, ', ') FROM \"Orders\" t GROUP BY t.\"CustomerId\" HAVING (COUNT(CASE WHEN t.\"Status\" = 1 THEN 1 END) > @p0) ORDER BY SUM(CASE WHEN NOT (t.\"IsDeleted\" = true) THEN t.\"Total\" END) DESC", report.ToSql());
        Assert.Contains("group_concat(CASE WHEN t.\"Status\" = 2 THEN t.\"Number\" END, ', ')", Session(SqlDialect.Sqlite).Query<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, Numbers = string.Join(", ", g.Where(o => o.Status == OrderStatus.Paid).Select(o => o.Number)) }).ToSql(), StringComparison.Ordinal);
        Assert.Contains("STRING_AGG(t.[Number], ';')", Session(SqlDialect.SqlServer).Query<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, Numbers = string.Join(";", g.Select(o => o.Number)) }).ToSql(), StringComparison.Ordinal);

        // A HAVING on the key, and the aggregates on a joined row's members.
        var byCountry = Session(SqlDialect.SqlServer).Query<OrderLine>()
            .Join(Session(SqlDialect.SqlServer).Query<Order>(), l => l.OrderId, o => o.Id, (l, o) => new { Line = l, Order = o })
            .GroupBy(x => x.Order.Customer!.Country)
            .Where(g => g.Key != "XX")
            .Select(g => new { Country = g.Key, Bolts = g.Where(x => x.Line.Sku == "BOLT").Sum(x => x.Line.Quantity), Orders = g.Select(x => x.Order.Id).Distinct().Count() });
        Assert.Equal("SELECT j1_Customer.[Country], SUM(CASE WHEN t.[Sku] = 'BOLT' THEN t.[Quantity] END), COUNT(DISTINCT j1.[Id]) FROM [OrderLines] t INNER JOIN [Orders] j1 ON j1.[Id] = t.[OrderId] INNER JOIN [Customers] j1_Customer ON j1_Customer.[Id] = j1.[CustomerId] GROUP BY j1_Customer.[Country] HAVING (j1_Customer.[Country] <> @p0)", byCountry.ToSql());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Extended_aggregates_compute_what_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var customers = seed.Customers.ToDictionary(c => c.Id);
        var orders = seed.Orders.ToDictionary(o => o.Id);

        var rows = await session.Query<Order>().GroupBy(o => o.CustomerId)
            .Where(g => g.Count(o => o.Status == OrderStatus.Open) > 2)
            .Select(g => new
            {
                Customer = g.Key,
                Open = g.Count(o => o.Status == OrderStatus.Open),
                Live = (decimal?)g.Where(o => !o.IsDeleted).Sum(o => o.Total),
                Statuses = g.Select(o => o.Status).Distinct().Count(),
                Numbers = string.Join(",", g.Where(o => o.Status == OrderStatus.Paid).Select(o => o.Number)),
            }).ToListAsync();
        var expected = seed.Orders.GroupBy(o => o.CustomerId).Where(g => g.Count(o => o.Status == OrderStatus.Open) > 2)
            .Select(g => (g.Key, g.Count(o => o.Status == OrderStatus.Open), g.Where(o => !o.IsDeleted).Select(o => (decimal?)o.Total).Sum(), g.Select(o => o.Status).Distinct().Count(), g.Where(o => o.Status == OrderStatus.Paid).Select(o => o.Number).OrderBy(n => n, StringComparer.Ordinal).ToList()))
            .OrderBy(r => r.Key).ToList();
        Assert.Equal(expected.Count, rows.Count);
        var actual = rows.Select(r => (r.Customer, r.Open, r.Live, r.Statuses, (r.Numbers ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(n => n, StringComparer.Ordinal).ToList())).OrderBy(r => r.Customer).ToList();
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Item1, actual[i].Customer);
            Assert.Equal(expected[i].Item2, actual[i].Open);
            Assert.Equal(expected[i].Item3, actual[i].Live);
            Assert.Equal(expected[i].Item4, actual[i].Statuses);
            Assert.Equal(expected[i].Item5, actual[i].Item5);
        }

        var byCountry = await session.Query<OrderLine>()
            .Join(session.Query<Order>(), l => l.OrderId, o => o.Id, (l, o) => new { Line = l, Order = o })
            .GroupBy(x => x.Order.Customer!.Country)
            .Select(g => new { Country = g.Key, Bolts = (int?)g.Where(x => x.Line.Sku == "BOLT").Sum(x => x.Line.Quantity), Orders = g.Select(x => x.Order.Id).Distinct().Count() }).ToListAsync();
        var expectedByCountry = seed.Lines.GroupBy(l => customers[orders[l.OrderId].CustomerId].Country).Select(g => (g.Key, g.Where(l => l.Sku == "BOLT").Select(l => (int?)l.Quantity).Sum(), g.Select(l => l.OrderId).Distinct().Count())).OrderBy(r => r.Key, StringComparer.Ordinal);
        Assert.Equal(expectedByCountry, byCountry.Select(r => (r.Country, r.Bolts, r.Orders)).OrderBy(r => r.Country, StringComparer.Ordinal));
    }
}
