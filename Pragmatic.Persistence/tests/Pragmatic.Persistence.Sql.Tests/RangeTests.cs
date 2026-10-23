using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Ranges and arrays: an owned <c>SqlDateRange</c> (two bound columns) whose <c>Contains</c> is PostgreSQL's <c>daterange @&gt;</c> over
///     the columns and the two bounds compared elsewhere (an emulation, said at the call site); a list in a predicate bound as an array
///     parameter on PostgreSQL (<c>= ANY</c>: one text, the command kept, the query admitted in a batch) and expanded per call elsewhere.
/// </summary>
[Collection("databases")]
public class RangeTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void A_range_and_a_list_render_per_dialect()
    {
        var today = new DateOnly(2026, 10, 19);
        Assert.EndsWith("WHERE (daterange(t.\"Active_From\", t.\"Active_To\", '[]') @> @p0)", Session(SqlDialect.PostgreSql).Query<Invoice>().Where((i, day) => i.Active.Contains(day), today).ToSql(), StringComparison.Ordinal);
        Assert.EndsWith("WHERE (((t.\"Active_From\" IS NULL OR t.\"Active_From\" <= @p0) AND (t.\"Active_To\" IS NULL OR @p0 <= t.\"Active_To\")))", Session(SqlDialect.Sqlite).Query<Invoice>().Where((i, day) => i.Active.Contains(day), today).ToSql(), StringComparison.Ordinal);
        Assert.EndsWith("WHERE (((t.[Active_From] IS NULL OR t.[Active_From] <= @p0) AND (t.[Active_To] IS NULL OR @p0 <= t.[Active_To])))", Session(SqlDialect.SqlServer).Query<Invoice>().Where((i, day) => i.Active.Contains(day), today).ToSql(), StringComparison.Ordinal);

        // A list: an array parameter on PostgreSQL, the placeholder of an expansion elsewhere; the runtime path renders the same.
        var ids = new List<Guid> { Guid.NewGuid() };
        var postgres = Session(SqlDialect.PostgreSql);
        Assert.EndsWith("WHERE (t.\"Id\" = ANY(@p0))", postgres.Query<Order>().Where((o, ids) => ids.Contains(o.Id), ids).ToSql(), StringComparison.Ordinal);
        Assert.EndsWith("WHERE (t.\"Id\" IN ({0}))", Session(SqlDialect.Sqlite).Query<Order>().Where((o, ids) => ids.Contains(o.Id), ids).ToSql(), StringComparison.Ordinal);
        IQueryable<Order> runtime = postgres.Query<Order>();
        Assert.Equal(postgres.Query<Order>().Where((o, ids) => ids.Contains(o.Id), ids).ToSql(), runtime.Where(o => ids.Contains(o.Id)).ToSql());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_range_contains_a_day_as_the_object_does_and_a_list_binds_as_an_array_on_postgresql(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var seed = databases.Seed;
        var number = "RNG-" + Guid.NewGuid().ToString("N")[..8];
        var invoices = new[]
        {
            new Invoice { Id = Guid.NewGuid(), Number = number, Total = 1m, Active = SqlDateRange.Between(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)) },
            new Invoice { Id = Guid.NewGuid(), Number = number, Total = 2m, Active = SqlDateRange.Between(null, new DateOnly(2026, 3, 31)) },
            new Invoice { Id = Guid.NewGuid(), Number = number, Total = 3m, Active = SqlDateRange.Between(new DateOnly(2026, 7, 1), null) },
            new Invoice { Id = Guid.NewGuid(), Number = number, Total = 4m, Active = SqlDateRange.Between(null, null) },
        };
        await session.InsertManyAsync(invoices);
        foreach (var day in new[] { new DateOnly(2025, 12, 31), new DateOnly(2026, 2, 1), new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 1), new DateOnly(2027, 1, 1) })
        {
            var expected = invoices.Where(i => i.Active.Contains(day)).Select(i => i.Total).Order().ToList();
            Assert.Equal(expected, await session.Query<Invoice>().Where((i, a) => i.Number == a.number && i.Active.Contains(a.day), (number, day)).OrderBy(i => i.Total).Select(i => i.Total).ToListAsync());
            IQueryable<Invoice> runtime = session.Query<Invoice>();
            Assert.Equal(expected.Count, await runtime.Where(i => i.Number == number && i.Active.Contains(day)).CountAsync());
        }

        // The range comes back as it went: an open bound as null.
        var back = await session.Query<Invoice>().Where((i, n) => i.Number == n, number).OrderBy(i => i.Total).ToListAsync();
        Assert.Equal(invoices.Select(i => (i.Active.From, i.Active.To)), back.Select(i => (i.Active.From, i.Active.To)));

        // Lists: a Guid list, an enum list (int[] on PostgreSQL), an empty list (nothing); the same on the runtime path; in a batch where the dialect admits it.
        var ids = seed.Orders.Take(5).Select(o => o.Id).ToList();
        var statuses = new[] { OrderStatus.Open, OrderStatus.Paid };
        Assert.Equal(5, await session.Query<Order>().CountAsync((o, ids) => ids.Contains(o.Id), ids));
        Assert.Equal(seed.Orders.Count(o => statuses.Contains(o.Status)), await session.Query<Order>().CountAsync((o, s) => s.Contains(o.Status), statuses));
        Assert.Equal(0, await session.Query<Order>().CountAsync((o, ids) => ids.Contains(o.Id), new List<Guid>()));
        IQueryable<Order> composed = session.Query<Order>();
        Assert.Equal(5, await composed.Where(o => ids.Contains(o.Id)).CountAsync());
        if (dialect == SqlDialect.PostgreSql)
        {
            var batch = session.Batch();
            var counted = batch.CountInIds(ids);
            var all = batch.CountOpenAbove(0m);
            await batch.ExecuteAsync();
            Assert.Equal(5, counted.Result);
            Assert.Equal(seed.Orders.Count(o => o.Status == OrderStatus.Open && !o.IsDeleted && o.Total > 0m), all.Result);
        }
        else
            Assert.Throws<NotSupportedException>(() => session.Batch().CountInIds(ids));
    }
}
