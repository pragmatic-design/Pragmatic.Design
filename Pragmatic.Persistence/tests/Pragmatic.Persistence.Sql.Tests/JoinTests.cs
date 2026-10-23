using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Explicit joins: INNER JOIN on a plain table, LEFT JOIN on a filtered inner chain (a derived table exposing its columns),
///     composite keys, the result selector over both rows, and what follows it (Skip, Take, Distinct, the terminals).
///     Compile-time only: the runtime path says so.
/// </summary>
[Collection("databases")]
public class JoinTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    public sealed class CustomerOrderDto
    {
        public string Name { get; set; } = "";
        public string? Number { get; set; }
        public decimal? Total { get; set; }
    }

    [Fact]
    public void Joins_translate_to_inner_and_left_joins_with_their_keys_on_every_dialect()
    {
        // INNER JOIN on the table itself: the outer's predicate in the WHERE, the inner's columns under j1, a navigation from the inner row hangs off j1.
        var inner = Session(SqlDialect.PostgreSql).Query<Order>().Where(o => !o.IsDeleted)
            .Join(Session(SqlDialect.PostgreSql).Query<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => new { o.Number, c.Name, c.Country });
        Assert.Equal("SELECT t.\"Number\", j1.\"Name\", j1.\"Country\" FROM \"Orders\" t INNER JOIN \"Customers\" j1 ON j1.\"Id\" = t.\"CustomerId\" WHERE (NOT (t.\"IsDeleted\" = @p0))", inner.ToSql());
        var throughInner = Session(SqlDialect.Sqlite).Query<OrderLine>().Join(Session(SqlDialect.Sqlite).Query<Order>(), l => l.OrderId, o => o.Id, (l, o) => new { l.Sku, Who = o.Customer!.Name });
        Assert.Equal("SELECT t.\"Sku\", j1_Customer.\"Name\" FROM \"OrderLines\" t INNER JOIN \"Orders\" j1 ON j1.\"Id\" = t.\"OrderId\" INNER JOIN \"Customers\" j1_Customer ON j1_Customer.\"Id\" = j1.\"CustomerId\"", throughInner.ToSql());

        // LEFT JOIN on a filtered inner chain: the filter in the ON clause (the unmatched customers keep their row, the key's index stays usable), the argument in this chain's slot.
        var left = Session(SqlDialect.SqlServer).Query<Customer>()
            .LeftJoin(Session(SqlDialect.SqlServer).Query<Order>().Where((o, status) => o.Status == status, OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, Number = o.Number, Total = (decimal?)o.Total })
            .Take(5);
        Assert.Equal("SELECT t.[Name], j1.[Number], j1.[Total] FROM [Customers] t LEFT JOIN [Orders] j1 ON j1.[CustomerId] = t.[Id] AND (j1.[Status] = @p0) ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT @p1 ROWS ONLY", left.ToSql());
        // An inner chain with a join of its own (a navigation in its filter) is a derived table, its alias inside being the join's.
        var derived = Session(SqlDialect.PostgreSql).Query<OrderLine>().Join(Session(SqlDialect.PostgreSql).Query<Order>().Where(o => o.Customer!.Country == "IT"), l => l.OrderId, o => o.Id, (l, o) => new { l.Sku, o.Number }).ToSql();
        Assert.Equal("SELECT t.\"Sku\", j1.c2 FROM \"OrderLines\" t INNER JOIN (SELECT j1.\"Id\" AS c0, j1.\"CustomerId\" AS c1, j1.\"Number\" AS c2, j1.\"Status\" AS c3, j1.\"Total\" AS c4, j1.\"CreatedAt\" AS c5, j1.\"IsDeleted\" AS c6 FROM \"Orders\" j1 INNER JOIN \"Customers\" j1_Customer ON j1_Customer.\"Id\" = j1.\"CustomerId\" WHERE (j1_Customer.\"Country\" = @p0)) j1 ON j1.c0 = t.\"OrderId\"", derived);

        // A composite key as a tuple, a DTO result, DISTINCT after the join.
        var composite = Session(SqlDialect.Sqlite).Query<Order>()
            .Join(Session(SqlDialect.Sqlite).Query<Order>(), o => (o.CustomerId, o.Status), p => (p.CustomerId, p.Status), (o, p) => new CustomerOrderDto { Name = o.Number, Number = p.Number, Total = p.Total })
            .Distinct();
        Assert.Equal("SELECT DISTINCT t.\"Number\", j1.\"Number\", j1.\"Total\" FROM \"Orders\" t INNER JOIN \"Orders\" j1 ON j1.\"CustomerId\" = t.\"CustomerId\" AND j1.\"Status\" = t.\"Status\"", composite.ToSql());

        // The runtime path does not join: it says so.
        IQueryable<Order> orders = Session(SqlDialect.Sqlite).Query<Order>();
        var error = Assert.Throws<NotSupportedException>(() => orders.Join(Session(SqlDialect.Sqlite).Query<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => c.Name).ToSql());
        Assert.Contains("'Join' is translated at compile time only", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Joins_compute_what_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var customers = seed.Customers.ToDictionary(c => c.Id);

        // INNER JOIN: every order with its customer's name and country.
        var expected = seed.Orders.Where(o => !o.IsDeleted).Select(o => (o.Number, customers[o.CustomerId].Name, customers[o.CustomerId].Country)).OrderBy(r => r.Number, StringComparer.Ordinal).ToList();
        var rows = await session.Query<Order>().Where(o => !o.IsDeleted).OrderBy(o => o.Number)
            .Join(session.Query<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => new { o.Number, c.Name, c.Country }).ToListAsync();
        Assert.Equal(expected, rows.Select(r => (r.Number, r.Name, r.Country)));

        // LEFT JOIN on the open orders: a customer without one keeps a row with nulls; the count is customers + extra open orders.
        var open = seed.Orders.Where(o => o.Status == OrderStatus.Open).ToList();
        var expectedLeft = seed.Customers.SelectMany(c => open.Where(o => o.CustomerId == c.Id).Select(o => (c.Name, (string?)o.Number, (decimal?)o.Total)).DefaultIfEmpty((c.Name, null, null))).OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Item2, StringComparer.Ordinal).ToList();
        var leftRows = await session.Query<Customer>()
            .LeftJoin(session.Query<Order>().Where((o, status) => o.Status == status, OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, o) => new CustomerOrderDto { Name = c.Name, Number = o.Number, Total = (decimal?)o.Total })
            .ToListAsync();
        Assert.Equal(expectedLeft, leftRows.Select(r => (r.Name, r.Number, r.Total)).OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Number, StringComparer.Ordinal));
        Assert.Equal(expectedLeft.Count, await session.Query<Customer>().LeftJoin(session.Query<Order>().Where((o, status) => o.Status == status, OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, Number = o.Number }).CountAsync());

        // The inner chain filtered through its own navigation (a derived table with a join inside), and a navigation from the inner row in the result (a join hanging off j1).
        var expectedLines = seed.Lines.Count(l => customers[seed.Orders.First(o => o.Id == l.OrderId).CustomerId].Country == "IT");
        Assert.Equal(expectedLines, await session.Query<OrderLine>().Join(session.Query<Order>().Where(o => o.Customer!.Country == "IT"), l => l.OrderId, o => o.Id, (l, o) => new { l.Sku, o.Number }).CountAsync());
        var withCountry = await session.Query<OrderLine>().Join(session.Query<Order>(), l => l.OrderId, o => o.Id, (l, o) => new { l.Sku, Country = o.Customer!.Country }).ToListAsync();
        Assert.Equal(expectedLines, withCountry.Count(r => r.Country == "IT"));
        Assert.Equal(seed.Lines.Count, withCountry.Count);

        // A composite key (a self-join on customer and status): each order paired with every order of the same customer and status, itself included.
        var pairs = seed.Orders.Sum(o => seed.Orders.Count(p => p.CustomerId == o.CustomerId && p.Status == o.Status));
        Assert.Equal(pairs, await session.Query<Order>().Join(session.Query<Order>(), o => (o.CustomerId, o.Status), p => (p.CustomerId, p.Status), (o, p) => new { Left = o.Number, Right = p.Number }).CountAsync());
    }
}
