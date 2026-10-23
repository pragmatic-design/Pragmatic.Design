using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Window functions in projections and orderings (ROW_NUMBER, RANK, DENSE_RANK, LAG, COUNT(*) OVER () for a page with its total,
///     SUM OVER a partition), refused in predicates; <c>DistinctOn(key, order)</c> as the first row per key (DISTINCT ON on PostgreSQL,
///     ROW_NUMBER() = 1 elsewhere, the entity's visibility rules inside): the "current per parent" of provisus without raw SQL.
/// </summary>
[Collection("databases")]
public class WindowTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void Windows_and_distinct_on_render_per_dialect()
    {
        // DistinctOn: the filters so far inside the derived table, the picks read with rn = 1, the rest of the chain on them.
        var postgres = Session(SqlDialect.PostgreSql).Query<Order>().Where(o => !o.IsDeleted).DistinctOn(o => o.CustomerId, o => Sql.By(Sql.Desc(o.CreatedAt), o.Number)).OrderBy(o => o.Number).Take(5).ToSql();
        Assert.Contains(" FROM (SELECT DISTINCT ON (t.\"CustomerId\") t.*, 1 AS rn FROM \"Orders\" t WHERE (NOT (t.\"IsDeleted\" = @p0)) ORDER BY t.\"CustomerId\", t.\"CreatedAt\" DESC, t.\"Number\") t WHERE (t.rn = 1) ORDER BY t.\"Number\" LIMIT @p1", postgres, StringComparison.Ordinal);
        var sqlite = Session(SqlDialect.Sqlite).Query<Order>().Where(o => !o.IsDeleted).DistinctOn(o => o.CustomerId, o => Sql.By(Sql.Desc(o.CreatedAt), o.Number)).OrderBy(o => o.Number).Take(5).ToSql();
        Assert.Contains(" FROM (SELECT t.*, ROW_NUMBER() OVER (PARTITION BY t.\"CustomerId\" ORDER BY t.\"CreatedAt\" DESC, t.\"Number\") AS rn FROM \"Orders\" t WHERE (NOT (t.\"IsDeleted\" = @p0))) t WHERE (t.rn = 1) ORDER BY t.\"Number\" LIMIT @p1", sqlite, StringComparison.Ordinal);
        var sqlServer = Session(SqlDialect.SqlServer).Query<Order>().DistinctOn(o => o.CustomerId, o => Sql.Desc(o.CreatedAt)).ToSql();
        Assert.EndsWith(" FROM (SELECT t.*, ROW_NUMBER() OVER (PARTITION BY t.[CustomerId] ORDER BY t.[CreatedAt] DESC) AS rn FROM [Orders] t) t WHERE (t.rn = 1)", sqlServer, StringComparison.Ordinal);

        // The entity's visibility rules go inside the derived table (they pick the candidates), once.
        var live = Session(SqlDialect.PostgreSql).Query<LiveOrder>().DistinctOn(o => o.CustomerId, o => Sql.Desc(o.CreatedAt)).ToSql();
        Assert.Contains("FROM \"Orders\" t WHERE (NOT (t.\"IsDeleted\" = @p0)) ORDER BY", live, StringComparison.Ordinal);
        Assert.Equal(1, live.Split("\"IsDeleted\" = ").Length - 1); // the rule's predicate once (the column itself is in the select list)

        // Windows in a projection: ROW_NUMBER and COUNT(*) OVER () as integers (a cast on PostgreSQL, whose bigint Npgsql reads as it is), LAG, a running SUM.
        var projected = Session(SqlDialect.PostgreSql).Query<Order>().Select(o => new { o.Id, N = Sql.RowNumber(o.CustomerId, o.Number), Total = Sql.CountOver(), Previous = (decimal?)Sql.Lag(o.Total, 1, o.CustomerId, o.Number), Running = Sql.SumOver(o.Total, o.CustomerId, o.Number) }).ToSql();
        Assert.StartsWith("SELECT t.\"Id\", CAST(ROW_NUMBER() OVER (PARTITION BY t.\"CustomerId\" ORDER BY t.\"Number\") AS integer), CAST(COUNT(*) OVER () AS integer), LAG(t.\"Total\", 1) OVER (PARTITION BY t.\"CustomerId\" ORDER BY t.\"Number\"), SUM(t.\"Total\") OVER (PARTITION BY t.\"CustomerId\" ORDER BY t.\"Number\") FROM \"Orders\" t", projected, StringComparison.Ordinal);
        var ranked = Session(SqlDialect.SqlServer).Query<Order>().Select(o => new { o.Id, R = Sql.DenseRank(o.Status), Total = Sql.CountOver() }).ToSql();
        Assert.StartsWith("SELECT t.[Id], CAST(DENSE_RANK() OVER (ORDER BY t.[Status]) AS int), CAST(COUNT(*) OVER () AS int) FROM [Orders] t", ranked, StringComparison.Ordinal); // bigint there too
        // NTILE (bigint on SQL Server: cast), LAST_VALUE over the whole partition (the default frame ends at the current row), a moving frame: the counts are literals.
        var framed = Session(SqlDialect.SqlServer).Query<Order>().Select(o => new { o.Id, B = Sql.Ntile(4, o.Number), L = Sql.LastValue(o.Total, o.CustomerId, o.Number), M = Sql.SumOver(o.Total, o.CustomerId, o.Number, 2) }).ToSql();
        Assert.StartsWith("SELECT t.[Id], CAST(NTILE(4) OVER (ORDER BY t.[Number]) AS int), LAST_VALUE(t.[Total]) OVER (PARTITION BY t.[CustomerId] ORDER BY t.[Number] ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING), SUM(t.[Total]) OVER (PARTITION BY t.[CustomerId] ORDER BY t.[Number] ROWS BETWEEN 2 PRECEDING AND CURRENT ROW) FROM [Orders] t", framed, StringComparison.Ordinal);

        // The runtime path renders the same windows (in an ordering here: a runtime projection is a declared DTO, not an anonymous object).
        var session = Session(SqlDialect.PostgreSql);
        IQueryable<Order> composed = session.Query<Order>();
        Assert.Equal(session.Query<Order>().OrderBy(o => Sql.RowNumber(o.CustomerId, o.Number)).ThenBy(o => o.Id).Take(3).ToSql(), composed.OrderBy(o => Sql.RowNumber(o.CustomerId, o.Number)).ThenBy(o => o.Id).Take(3).ToSql());
        Assert.Equal(session.Query<Order>().OrderBy(o => Sql.Ntile(3, o.CustomerId, o.Number)).ThenBy(o => o.Id).Take(3).ToSql(), composed.OrderBy(o => Sql.Ntile(3, o.CustomerId, o.Number)).ThenBy(o => o.Id).Take(3).ToSql());
        var buckets = 5; // a captured count: the runtime path writes its value, a shape of its own
        Assert.Contains("NTILE(5) OVER", composed.OrderBy(o => Sql.Ntile(buckets, o.Number)).ToSql(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Windows_and_distinct_on_compute_what_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var customerId = seed.BusyCustomer.Id;
        var orders = seed.Orders.Where(o => o.CustomerId == customerId).OrderBy(o => o.Number, StringComparer.Ordinal).ToList();

        // The latest live order per customer ("current per parent"), then ordered and counted: the same picks as in memory.
        var latest = await session.Query<Order>().Where(o => !o.IsDeleted).DistinctOn(o => o.CustomerId, o => Sql.By(Sql.Desc(o.CreatedAt), o.Number)).OrderBy(o => o.Number).Select(o => new { o.CustomerId, o.Number }).ToListAsync();
        var expectedLatest = seed.Orders.Where(o => !o.IsDeleted).GroupBy(o => o.CustomerId).Select(g => g.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Number, StringComparer.Ordinal).First()).OrderBy(o => o.Number, StringComparer.Ordinal).Select(o => (o.CustomerId, o.Number)).ToList();
        Assert.Equal(expectedLatest, latest.Select(l => (l.CustomerId, l.Number)));
        Assert.Equal(expectedLatest.Count, await session.Query<Order>().Where(o => !o.IsDeleted).DistinctOn(o => o.CustomerId, o => Sql.Desc(o.CreatedAt)).CountAsync());
        Assert.Equal(seed.Orders.Where(o => !o.IsDeleted).Select(o => o.CustomerId).Distinct().Count(), await session.Query<LiveOrder>().DistinctOn(o => o.CustomerId, o => Sql.Desc(o.CreatedAt)).CountAsync());

        // A page with its total in one round trip.
        var page = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number).Skip(2).Take(3).Select(o => new { o.Number, Total = Sql.CountOver() }).ToListAsync();
        Assert.Equal(orders.Skip(2).Take(3).Select(o => o.Number), page.Select(p => p.Number));
        Assert.All(page, p => Assert.Equal(orders.Count, p.Total));

        // Row numbers, a dense rank by status, the previous total, the running total: per customer, in number order.
        var windows = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number)
            .Select(o => new { o.Number, N = Sql.RowNumber(o.CustomerId, o.Number), Rank = Sql.DenseRank(o.Status), Previous = (decimal?)Sql.Lag(o.Total, 1, o.CustomerId, o.Number), Running = Sql.SumOver(o.Total, o.CustomerId, o.Number) }).ToListAsync();
        var statuses = orders.Select(o => o.Status).Distinct().OrderBy(s => (int)s).ToList();
        var running = 0m;
        var expectedWindows = orders.Select((o, i) => (o.Number, i + 1, statuses.IndexOf(o.Status) + 1, i == 0 ? (decimal?)null : orders[i - 1].Total, Math.Round(running += o.Total, 2))).ToList();
        Assert.Equal(expectedWindows, windows.Select(w => (w.Number, w.N, w.Rank, w.Previous, Math.Round(w.Running, 2))));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Buckets_edges_and_moving_frames_compute_what_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var customerId = seed.BusyCustomer.Id;
        var orders = seed.Orders.Where(o => o.CustomerId == customerId).OrderBy(o => o.Number, StringComparer.Ordinal).ToList();

        // NTILE(4): the first (n % 4) buckets one row larger; FIRST_VALUE and LAST_VALUE over the whole partition; a sum over the row and the two before it.
        var windows = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number)
            .Select(o => new { o.Number, Bucket = Sql.Ntile(4, o.CustomerId, o.Number), First = Sql.FirstValue(o.Total, o.CustomerId, o.Number), Last = Sql.LastValue(o.Total, o.CustomerId, o.Number), Moving = Sql.SumOver(o.Total, o.CustomerId, o.Number, 2) }).ToListAsync();
        int Bucket(int i)
        {
            int size = orders.Count / 4, larger = orders.Count % 4;
            return i < larger * (size + 1) ? i / (size + 1) + 1 : larger + (i - larger * (size + 1)) / size + 1;
        }

        var expected = orders.Select((o, i) => (o.Number, Bucket(i), orders[0].Total, orders[^1].Total, Math.Round(orders.Skip(Math.Max(0, i - 2)).Take(Math.Min(i, 2) + 1).Sum(x => x.Total), 2))).ToList();
        Assert.Equal(expected, windows.Select(w => (w.Number, w.Bucket, w.First, w.Last, Math.Round(w.Moving, 2))));
        Assert.Equal(4, windows.Max(w => w.Bucket));

        // AVG over a moving ROWS frame (as a double), MIN over the running frame, MAX and COUNT(*) over the partition.
        var aggregates = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number)
            .Select(o => new { o.Number, Mean = Sql.AvgOver(o.Total, o.CustomerId, o.Number, 2), Lowest = Sql.MinOver(o.Total, o.CustomerId, o.Number), Highest = Sql.MaxOver(o.Total, o.CustomerId), Rows = Sql.CountOver(o.CustomerId) }).ToListAsync();
        var expectedAggregates = orders.Select((o, i) =>
        {
            var frame = orders.Skip(Math.Max(0, i - 2)).Take(Math.Min(i, 2) + 1).ToList();
            return (o.Number, Math.Round((double)frame.Sum(x => x.Total) / frame.Count, 6), orders.Take(i + 1).Min(x => x.Total), orders.Max(x => x.Total), orders.Count);
        }).ToList();
        Assert.Equal(expectedAggregates, aggregates.Select(a => (a.Number, Math.Round(a.Mean, 6), a.Lowest, a.Highest, a.Rows)));

        // A named window shared by three functions: one WINDOW clause, each function OVER its name; the values those of the inline windows.
        var shared = session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number)
            .Select(o => new { o.Number, N = Sql.RowNumber(Sql.Window(o.CustomerId, o.Number)), Running = Sql.SumOver(o.Total, Sql.Window(o.CustomerId, o.Number)), Mean = Sql.AvgOver(o.Total, Sql.Window(o.CustomerId, o.Number)) });
        var sql = shared.ToSql();
        Assert.Equal(1, sql.Split(" WINDOW w1 AS (PARTITION BY ").Length - 1);
        Assert.Equal(3, sql.Split("OVER w1").Length - 1);
        var named = await shared.ToListAsync();
        var running = 0m;
        var expectedNamed = orders.Select((o, i) => (o.Number, i + 1, Math.Round(running += o.Total, 2), Math.Round((double)running / (i + 1), 6))).ToList();
        Assert.Equal(expectedNamed, named.Select(n => (n.Number, n.N, Math.Round(n.Running, 2), Math.Round(n.Mean, 6))));

        // RANGE frames with an offset on the order's value: ties included, gaps honoured (SQL Server: a correlated subquery under the same predicates).
        var ranged = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number)
            .Select(o => new { o.Number, Near = Sql.SumOverRange(o.Total, o.CustomerId, o.Total, 100), Within = Sql.CountOverRange(o.CustomerId, o.Total, 100), Lowest = Sql.MinOverRange(o.Total, o.CustomerId, o.Total, 100) }).ToListAsync();
        var expectedRanged = orders.Select(o =>
        {
            var near = orders.Where(x => x.Total >= o.Total - 100 && x.Total <= o.Total).ToList();
            return (o.Number, Math.Round(near.Sum(x => x.Total), 2), near.Count, near.Min(x => x.Total));
        }).ToList();
        Assert.Equal(expectedRanged, ranged.Select(r => (r.Number, Math.Round(r.Near, 2), r.Within, r.Lowest)));

        // The runtime path: the same frame in an ordering.
        IQueryable<Order> composed = session.Query<Order>();
        var byNear = await composed.Where(o => o.CustomerId == customerId).OrderBy(o => Sql.CountOverRange(o.CustomerId, o.Total, 100)).ThenBy(o => o.Number).ToListAsync();
        Assert.Equal(orders.OrderBy(o => orders.Count(x => x.Total >= o.Total - 100 && x.Total <= o.Total)).ThenBy(o => o.Number, StringComparer.Ordinal).Select(o => o.Id), byNear.Select(o => o.Id));
    }
}
