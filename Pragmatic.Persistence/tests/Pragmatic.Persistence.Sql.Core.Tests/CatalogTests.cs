using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     The typed catalog (<c>Sql.*</c>): each function rendered for the three dialects (translation), the same rows the
///     seed computes in memory (equivalence on the three databases), and the runtime path rendering the same SQL.
/// </summary>
[Collection("databases")]
public class CatalogTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void A_catalog_function_renders_per_dialect_in_predicates_orderings_and_projections()
    {
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // A predicate: the dialect pieces resolve where the statement is written; the constants stay parameters.
        var sqlite = Session(SqlDialect.Sqlite).Query<Order>().Where((o, epoch) => Sql.Year(o.CreatedAt) == 2026 && Sql.Length(o.Number) > 5 && Sql.DateDiff(DatePart.Day, epoch, o.CreatedAt) >= 100, epoch).ToSql();
        Assert.Contains("CAST(strftime('%Y', t.\"CreatedAt\") AS INTEGER) = @p0", sqlite, StringComparison.Ordinal);
        Assert.Contains("LENGTH(t.\"Number\") > @p1", sqlite, StringComparison.Ordinal);
        Assert.Contains("CAST((julianday(t.\"CreatedAt\") - julianday(@p2)) * 1 AS INTEGER) >= @p3", sqlite, StringComparison.Ordinal);
        var postgres = Session(SqlDialect.PostgreSql).Query<Order>().Where((o, epoch) => Sql.Year(o.CreatedAt) == 2026 && Sql.Length(o.Number) > 5 && Sql.DateDiff(DatePart.Day, epoch, o.CreatedAt) >= 100, epoch).ToSql();
        Assert.Contains("CAST(EXTRACT(YEAR FROM t.\"CreatedAt\") AS integer) = @p0", postgres, StringComparison.Ordinal);
        Assert.Contains("CAST(TRUNC(EXTRACT(EPOCH FROM (t.\"CreatedAt\" - @p2)) / 86400) AS integer) >= @p3", postgres, StringComparison.Ordinal);
        var sqlServer = Session(SqlDialect.SqlServer).Query<Order>().Where((o, epoch) => Sql.Year(o.CreatedAt) == 2026 && Sql.Length(o.Number) > 5 && Sql.DateDiff(DatePart.Day, epoch, o.CreatedAt) >= 100, epoch).ToSql();
        Assert.Contains("DATEPART(year, t.[CreatedAt]) = @p0", sqlServer, StringComparison.Ordinal);
        Assert.Contains("LEN(t.[Number]) > @p1", sqlServer, StringComparison.Ordinal);
        Assert.Contains("DATEDIFF(day, @p2, t.[CreatedAt]) >= @p3", sqlServer, StringComparison.Ordinal);
        // A function inside a function: the inner piece is resolved inside the outer.
        Assert.Contains("ROUND(ABS(t.\"Total\"), 1)", Session(SqlDialect.Sqlite).Query<Order>().Select(o => new { Value = Sql.Round(Sql.Abs(o.Total), 1) }).ToSql(), StringComparison.Ordinal);

        // An ordering and a projection: compile-time constants are literals there (no parameter shapes an ORDER BY or a select list).
        var ordered = Session(SqlDialect.PostgreSql).Query<Order>().OrderBy(o => Sql.Substring(o.Number, 4, 2)).ThenBy(o => o.Id).Take(5);
        Assert.EndsWith("ORDER BY SUBSTRING(t.\"Number\" FROM 4 + 1 FOR 2), t.\"Id\" LIMIT @p0", ordered.ToSql(), StringComparison.Ordinal);
        var projected = Session(SqlDialect.SqlServer).Query<Order>().Where((o, id) => o.Id == id, Guid.NewGuid()).Select(o => new { Short = Sql.Replace(o.Number, "ORD-", ""), Rounded = Sql.Round(o.Total, 1), Who = Sql.Coalesce(o.Customer!.Name, "none") });
        Assert.StartsWith("SELECT REPLACE(t.[Number], 'ORD-', ''), ROUND(t.[Total], 1), COALESCE(j_Customer.[Name], 'none') FROM [Orders] t INNER JOIN [Customers] j_Customer", projected.ToSql(), StringComparison.Ordinal);
        Assert.Contains("ROUND(CAST(t.\"Total\" AS numeric), 1)", Session(SqlDialect.PostgreSql).Query<Order>().Select(o => new { Rounded = Sql.Round(o.Total, 1) }).ToSql(), StringComparison.Ordinal);

        // Like takes the caller's pattern as it is; the runtime path renders the same statements.
        var like = Session(SqlDialect.Sqlite).Query<Order>().Where((o, pattern) => Sql.Like(o.Number, pattern), "ORD-1000%");
        Assert.EndsWith("WHERE (t.\"Number\" LIKE @p0)", like.ToSql(), StringComparison.Ordinal);
        var session = Session(SqlDialect.PostgreSql);
        IQueryable<Order> composed = session.Query<Order>();
        Assert.Equal(session.Query<Order>().Where((o, epoch) => Sql.Year(o.CreatedAt) == 2026 && Sql.DateDiff(DatePart.Day, epoch, o.CreatedAt) >= 100, epoch).ToSql(), composed.Where(o => Sql.Year(o.CreatedAt) == 2026 && Sql.DateDiff(DatePart.Day, epoch, o.CreatedAt) >= 100).ToSql());
        Assert.Equal(session.Query<Order>().OrderBy(o => Sql.Length(o.Number)).ThenBy(o => o.Id).ToSql(), composed.OrderBy(o => Sql.Length(o.Number)).ThenBy(o => o.Id).ToSql());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Catalog_functions_compute_what_the_seed_computes_in_memory(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        var customerId = seed.BusyCustomer.Id;
        using var session = databases.Session(dialect);
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Dates: parts, a difference in whole days from midnight UTC, the clock, an addition.
        Assert.Equal(seed.Orders.Count(o => o.CreatedAt.Month == 3), await session.Query<Order>().CountAsync(o => Sql.Month(o.CreatedAt) == 3));
        Assert.Equal(seed.Orders.Count(o => o.CreatedAt.Day == 15 && o.CreatedAt.Hour < 12), await session.Query<Order>().CountAsync(o => Sql.Day(o.CreatedAt) == 15 && Sql.Hour(o.CreatedAt) < 12));
        Assert.Equal(seed.Orders.Count(o => (int)Math.Floor((o.CreatedAt - epoch).TotalDays) >= 100), await session.Query<Order>().CountAsync((o, epoch) => Sql.DateDiff(DatePart.Day, epoch, o.CreatedAt) >= 100, epoch));
        Assert.Equal(seed.Orders.Count(o => (int)Math.Floor((o.CreatedAt - epoch).TotalHours) >= 2400), await session.Query<Order>().CountAsync((o, epoch) => Sql.DateDiff(DatePart.Hour, epoch, o.CreatedAt) >= 2400, epoch));
        Assert.Equal(seed.Orders.Count, await session.Query<Order>().CountAsync(o => o.CreatedAt < Sql.Now() && Sql.DateAdd(DatePart.Year, 10, o.CreatedAt) > Sql.UtcNow()));

        // Strings: length, substring (zero-based), replace, concat, trim, LIKE with the caller's wildcards.
        Assert.Equal(seed.Orders.Count, await session.Query<Order>().CountAsync(o => Sql.Length(o.Number) == 10));
        Assert.Equal(seed.Orders.Count(o => o.Number.Substring(4, 2) == "10"), await session.Query<Order>().CountAsync(o => Sql.Substring(o.Number, 4, 2) == "10"));
        Assert.Equal(seed.Orders.Count(o => o.Number.StartsWith("ORD-1000", StringComparison.Ordinal)), await session.Query<Order>().CountAsync((o, pattern) => Sql.Like(o.Number, pattern), "ORD-1000%"));
        var strings = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number).Take(3).Select(o => new { Short = Sql.Replace(o.Number, "ORD-", ""), Tagged = Sql.Concat(o.Number, "-x"), Trimmed = Sql.Trim(o.Number), Who = Sql.Coalesce(o.Customer!.Name, "none") }).ToListAsync();
        var expectedStrings = seed.Orders.Where(o => o.CustomerId == customerId).OrderBy(o => o.Number, StringComparer.Ordinal).Take(3).Select(o => (o.Number.Replace("ORD-", ""), o.Number + "-x", o.Number, seed.BusyCustomer.Name));
        Assert.Equal(expectedStrings, strings.Select(s => (s.Short, s.Tagged, s.Trimmed, s.Who)));

        // Numbers: round, abs, ceiling, floor in a projection, half away from zero as SQL does; ordering by a function.
        var numbers = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => Sql.Length(o.Number)).ThenBy(o => o.Number).Take(5).Select(o => new { Rounded = Sql.Round(o.Total, 0), Up = Sql.Ceiling(o.Total), Down = Sql.Floor(o.Total), Same = Sql.Abs(o.Total) }).ToListAsync();
        var expectedNumbers = seed.Orders.Where(o => o.CustomerId == customerId).OrderBy(o => o.Number.Length).ThenBy(o => o.Number, StringComparer.Ordinal).Take(5).Select(o => (Math.Round(o.Total, 0, MidpointRounding.AwayFromZero), Math.Ceiling(o.Total), Math.Floor(o.Total), o.Total));
        Assert.Equal(expectedNumbers, numbers.Select(n => (n.Rounded, n.Up, n.Down, n.Same)));

        // The runtime path: the same functions through the registered maps, the shape cache hit on the second call.
        IQueryable<Order> composed = session.Query<Order>();
        Assert.Equal(seed.Orders.Count(o => o.CreatedAt.Month == 3), await composed.Where(o => Sql.Month(o.CreatedAt) == 3).CountAsync());
        Assert.Equal(seed.Orders.Count(o => o.CreatedAt.Month == 4), await composed.Where(o => Sql.Month(o.CreatedAt) == 4).CountAsync());
        var runtimeOrdered = await composed.Where(o => o.CustomerId == customerId).OrderBy(o => Sql.Substring(o.Number, 4, 2)).ThenBy(o => o.Id).Take(3).ToListAsync();
        Assert.Equal(3, runtimeOrdered.Count);
    }

    [Fact]
    public void The_rest_of_the_catalog_renders_per_dialect()
    {
        // Patterns: a regular expression (REGEXP needs a function on SQLite's connection, REGEXP_LIKE SQL Server 2025: both said as emulations), LIKE with the caller's escape.
        Assert.EndsWith("WHERE (t.\"Number\" REGEXP @p0)", Session(SqlDialect.Sqlite).Query<Order>().Where((o, p) => Sql.Matches(o.Number, p), "^ORD").ToSql(), StringComparison.Ordinal);
        Assert.EndsWith("WHERE (t.\"Number\" ~ @p0)", Session(SqlDialect.PostgreSql).Query<Order>().Where((o, p) => Sql.Matches(o.Number, p), "^ORD").ToSql(), StringComparison.Ordinal);
        Assert.EndsWith("WHERE (REGEXP_LIKE(t.[Number], @p0) = 1)", Session(SqlDialect.SqlServer).Query<Order>().Where((o, p) => Sql.Matches(o.Number, p), "^ORD").ToSql(), StringComparison.Ordinal);
        Assert.EndsWith("WHERE (t.\"Number\" LIKE @p0 ESCAPE @p1)", Session(SqlDialect.PostgreSql).Query<Order>().Where((o, p) => Sql.Like(o.Number, p, "\\"), "ORD\\_1%").ToSql(), StringComparison.Ordinal);

        // Comparisons: BETWEEN, a null-safe inequality (IS DISTINCT FROM; IS NOT on SQLite).
        Assert.EndsWith("WHERE (t.\"Total\" BETWEEN @p0 AND @p1)", Session(SqlDialect.Sqlite).Query<Order>().Where((o, a) => Sql.Between(o.Total, a.low, a.high), (low: 10m, high: 20m)).ToSql(), StringComparison.Ordinal);
        Assert.EndsWith("WHERE (t.\"Number\" IS NOT @p0)", Session(SqlDialect.Sqlite).Query<Order>().Where((o, n) => Sql.IsDistinctFrom(o.Number, n), "x").ToSql(), StringComparison.Ordinal);
        Assert.EndsWith("WHERE (t.\"Number\" IS DISTINCT FROM @p0)", Session(SqlDialect.PostgreSql).Query<Order>().Where((o, n) => Sql.IsDistinctFrom(o.Number, n), "x").ToSql(), StringComparison.Ordinal);
        Assert.EndsWith("WHERE (t.[Number] IS DISTINCT FROM @p0)", Session(SqlDialect.SqlServer).Query<Order>().Where((o, n) => Sql.IsDistinctFrom(o.Number, n), "x").ToSql(), StringComparison.Ordinal);

        // Values: NULLIF, GREATEST/LEAST (MAX/MIN as scalars on SQLite), casts to text, integer, decimal, a date as ISO text.
        var sqlite = Session(SqlDialect.Sqlite).Query<Order>().Select(o => new { A = Sql.NullIf(o.Total, 0m), B = Sql.Greatest(o.Total, 100m), C = Sql.Least(o.Total, 100m), D = Sql.ToText(o.Status), E = Sql.ToInt32(Sql.Substring(o.Number, 4, 5)), F = Sql.ToDecimal(Sql.Substring(o.Number, 4, 5)), G = Sql.DateText(o.CreatedAt) }).ToSql();
        Assert.StartsWith("SELECT NULLIF(t.\"Total\", 0), MAX(t.\"Total\", 100), MIN(t.\"Total\", 100), CAST(t.\"Status\" AS TEXT), CAST(SUBSTR(t.\"Number\", 4 + 1, 5) AS INTEGER), CAST(SUBSTR(t.\"Number\", 4 + 1, 5) AS REAL), strftime('%Y-%m-%d', t.\"CreatedAt\") FROM", sqlite, StringComparison.Ordinal);
        var postgres = Session(SqlDialect.PostgreSql).Query<Order>().Select(o => new { B = Sql.Greatest(o.Total, 100m), D = Sql.ToText(o.Status), E = Sql.ToInt32(o.Number), F = Sql.ToDecimal(o.Number), G = Sql.DateText(o.CreatedAt) }).ToSql();
        Assert.StartsWith("SELECT GREATEST(t.\"Total\", 100), CAST(t.\"Status\" AS text), CAST(t.\"Number\" AS integer), CAST(t.\"Number\" AS numeric), to_char(t.\"CreatedAt\", 'YYYY-MM-DD') FROM", postgres, StringComparison.Ordinal);
        var sqlServer = Session(SqlDialect.SqlServer).Query<Order>().Select(o => new { C = Sql.Least(o.Total, 100m), D = Sql.ToText(o.Status), E = Sql.ToInt32(o.Number), F = Sql.ToDecimal(o.Number), G = Sql.DateText(o.CreatedAt) }).ToSql();
        Assert.StartsWith("SELECT LEAST(t.[Total], 100), CAST(t.[Status] AS nvarchar(max)), CAST(t.[Number] AS int), CAST(t.[Number] AS decimal(18, 2)), CONVERT(varchar(10), t.[CreatedAt], 23) FROM", sqlServer, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task The_rest_of_the_catalog_computes_what_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        var customerId = seed.BusyCustomer.Id;
        var connection = databases.Open(dialect);
        if (connection is Microsoft.Data.Sqlite.SqliteConnection sqlite)
            sqlite.CreateFunction("regexp", (string pattern, string input) => System.Text.RegularExpressions.Regex.IsMatch(input, pattern)); // SQLite's REGEXP is the application's function
        using var session = new SqlSession(connection, dialect);

        Assert.Equal(seed.Orders.Count(o => o.Total >= 100m && o.Total <= 200m), await session.Query<Order>().CountAsync((o, a) => Sql.Between(o.Total, a.low, a.high), (low: 100m, high: 200m)));
        Assert.Equal(seed.Orders.Count - 1, await session.Query<Order>().CountAsync((o, n) => Sql.IsDistinctFrom(o.Number, n), "ORD-100000")); // the seed's first number: one row is not distinct from it
        // LIKE with an escape: an escaped underscore is a literal (no number has one), an unescaped one a wildcard.
        Assert.Equal(0, await session.Query<Order>().CountAsync((o, p) => Sql.Like(o.Number, p, "\\"), "ORD\\_1%"));
        Assert.Equal(seed.Orders.Count(o => o.Number.StartsWith("ORD-1", StringComparison.Ordinal)), await session.Query<Order>().CountAsync((o, p) => Sql.Like(o.Number, p, "\\"), "ORD_1%"));
        if (dialect != SqlDialect.SqlServer) // REGEXP_LIKE is SQL Server 2025's; the container is 2022
            Assert.Equal(seed.Orders.Count(o => System.Text.RegularExpressions.Regex.IsMatch(o.Number, "^ORD-1000[0-9]$")), await session.Query<Order>().CountAsync((o, p) => Sql.Matches(o.Number, p), "^ORD-1000[0-9]$"));

        var values = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number).Take(3)
            .Select(o => new { Nulled = Sql.NullIf(o.Number, "ORD-100000"), High = Sql.Greatest(o.Total, 100m), Low = Sql.Least(o.Total, 100m), Text = Sql.ToText(o.Status), Day = Sql.DateText(o.CreatedAt), Number = Sql.ToInt32(Sql.Substring(o.Number, 4, 6)) }).ToListAsync();
        var expected = seed.Orders.Where(o => o.CustomerId == customerId).OrderBy(o => o.Number, StringComparer.Ordinal).Take(3)
            .Select(o => (o.Number == "ORD-100000" ? null : o.Number, Math.Max(o.Total, 100m), Math.Min(o.Total, 100m), ((int)o.Status).ToString(), o.CreatedAt.ToString("yyyy-MM-dd"), int.Parse(o.Number.Substring(4, 6))));
        Assert.Equal(expected, values.Select(v => (v.Nulled, v.High, v.Low, v.Text, v.Day, v.Number)));
    }
}
