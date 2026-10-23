using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>The application's own SQL functions: declared with [SqlFunction], defined with typed fragments per dialect, read by the generator and never run.</summary>
public static partial class Fx
{
    /// <summary>Whole days between two instants, native on SQL Server, MySQL and PostgreSQL, emulated on SQLite (dates are text there).</summary>
    [SqlFunction]
    public static partial int DaysBetween(DateTimeOffset from, DateTimeOffset to);

    private static SqlFragment DaysBetween(SqlDialect dialect, SqlFragment from, SqlFragment to) => dialect switch
    {
        SqlDialect.SqlServer => Fragment.Call("DATEDIFF", Fragment.Keyword("day"), from, to),
        SqlDialect.MySql => Fragment.Call("TIMESTAMPDIFF", Fragment.Keyword("DAY"), from, to),
        SqlDialect.Oracle => Fragment.Extract(DateField.Day, to - from), // the interval's whole days
        SqlDialect.PostgreSql => Fragment.Cast(Fragment.Call("TRUNC", Fragment.Extract(DateField.Epoch, to - from) / 86400), SqlType.Integer),
        _ => Fragment.Emulated(Fragment.Cast(Fragment.Call("julianday", to) - Fragment.Call("julianday", from), SqlType.Integer), "dates are ISO text on SQLite: julianday reads them"),
    };

    /// <summary>A predicate function: true for a ten-character number (LEN on SQL Server, LENGTH elsewhere).</summary>
    [SqlFunction]
    public static partial bool IsOrderNumber(string number);

    private static SqlFragment IsOrderNumber(SqlDialect dialect, SqlFragment number) => dialect switch
    {
        SqlDialect.SqlServer => Fragment.Equal(Fragment.Call("LEN", number), 10),
        SqlDialect.Sqlite or SqlDialect.PostgreSql => Fragment.Equal(Fragment.Call("LENGTH", number), 10),
        _ => Fragment.Equal(Fragment.Call("LENGTH", number), 10),
    };

    /// <summary>The same rendering on every dialect: no switch.</summary>
    [SqlFunction]
    public static partial decimal Twice(decimal value);

    private static SqlFragment Twice(SqlDialect dialect, SqlFragment value) => value * 2;
}

[Collection("databases")]
public class AppFunctionTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void An_application_function_renders_its_definition_per_dialect_and_never_runs()
    {
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Contains("DATEDIFF(day, @p0, t.[CreatedAt]) >= @p1", Session(SqlDialect.SqlServer).Query<Order>().Where((o, epoch) => Fx.DaysBetween(epoch, o.CreatedAt) >= 100, epoch).ToSql(), StringComparison.Ordinal);
        Assert.Contains("CAST(TRUNC((EXTRACT(EPOCH FROM (t.\"CreatedAt\" - @p0)) / 86400)) AS INTEGER) >= @p1", Session(SqlDialect.PostgreSql).Query<Order>().Where((o, epoch) => Fx.DaysBetween(epoch, o.CreatedAt) >= 100, epoch).ToSql(), StringComparison.Ordinal);
        Assert.Contains("CAST((julianday(t.\"CreatedAt\") - julianday(@p0)) AS INTEGER) >= @p1", Session(SqlDialect.Sqlite).Query<Order>().Where((o, epoch) => Fx.DaysBetween(epoch, o.CreatedAt) >= 100, epoch).ToSql(), StringComparison.Ordinal);
        Assert.Contains("WHERE (LEN(t.[Number]) = 10)", Session(SqlDialect.SqlServer).Query<Order>().Where(o => Fx.IsOrderNumber(o.Number)).ToSql(), StringComparison.Ordinal);
        Assert.Contains("WHERE (LENGTH(t.\"Number\") = 10)", Session(SqlDialect.PostgreSql).Query<Order>().Where(o => Fx.IsOrderNumber(o.Number)).ToSql(), StringComparison.Ordinal);
        Assert.Contains("SELECT (t.\"Total\" * 2) FROM", Session(SqlDialect.Sqlite).Query<Order>().Select(o => Fx.Twice(o.Total)).ToSql(), StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => Fx.Twice(1m)); // the generated body: SQL only
        // The runtime path reads the registered renderer: the same statement.
        var session = Session(SqlDialect.PostgreSql);
        IQueryable<Order> composed = session.Query<Order>();
        Assert.Equal(session.Query<Order>().Where((o, epoch) => Fx.DaysBetween(epoch, o.CreatedAt) >= 100 && Fx.IsOrderNumber(o.Number), epoch).ToSql(), composed.Where(o => Fx.DaysBetween(epoch, o.CreatedAt) >= 100 && Fx.IsOrderNumber(o.Number)).ToSql());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Application_functions_compute_what_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(seed.Orders.Count(o => (int)Math.Floor((o.CreatedAt - epoch).TotalDays) >= 100), await session.Query<Order>().CountAsync((o, epoch) => Fx.DaysBetween(epoch, o.CreatedAt) >= 100, epoch));
        Assert.Equal(seed.Orders.Count, await session.Query<Order>().CountAsync(o => Fx.IsOrderNumber(o.Number)));
        var doubled = await session.Query<Order>().Where((o, id) => o.Id == id, seed.Orders[7].Id).Select(o => Fx.Twice(o.Total)).FirstOrDefaultAsync();
        Assert.Equal(seed.Orders[7].Total * 2, doubled);
        IQueryable<Order> composed = session.Query<Order>();
        Assert.Equal(seed.Orders.Count(o => (int)Math.Floor((o.CreatedAt - epoch).TotalDays) >= 200), await composed.Where(o => Fx.DaysBetween(epoch, o.CreatedAt) >= 200).CountAsync());
    }
}
