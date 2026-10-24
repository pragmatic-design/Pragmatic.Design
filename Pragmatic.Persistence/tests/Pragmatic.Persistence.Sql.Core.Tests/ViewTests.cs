using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     Sources that are not tables: a view read like a table and never written (<c>[Table(ReadOnly = true)]</c>: no writer, the
///     session refuses to write it), and a table-valued function as the root of a chain (<c>session.Function&lt;T&gt;("name", args)</c>),
///     with navigations and an explicit join on its rows.
/// </summary>
[Collection("databases")]
public class ViewTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void A_function_root_renders_per_dialect_and_a_view_has_no_writer()
    {
        var id = Guid.NewGuid();
        Assert.EndsWith(" FROM orders_of(@p0) t WHERE (NOT (t.\"IsDeleted\" = @p1)) ORDER BY t.\"Number\" LIMIT @p2", Session(SqlDialect.PostgreSql).Function<Order>("orders_of", id).Where(o => !o.IsDeleted).OrderBy(o => o.Number).Take(5).ToSql(), StringComparison.Ordinal);
        Assert.Contains(" FROM dbo.orders_of(@p0) t INNER JOIN [Customers] j_Customer ON j_Customer.[Id] = t.[CustomerId] WHERE ", Session(SqlDialect.SqlServer).Function<Order>("orders_of", id).Where(o => o.Customer!.Country == "IT").ToSql(), StringComparison.Ordinal);
        Assert.Equal("SELECT t.\"Number\", j1.\"Name\" FROM orders_of(@p0) t INNER JOIN \"Customers\" j1 ON j1.\"Id\" = t.\"CustomerId\"", Session(SqlDialect.PostgreSql).Function<Order>("orders_of", id).Join(Session(SqlDialect.PostgreSql).Query<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => new { o.Number, c.Name }).ToSql());
        Assert.Null(SqlEntityMap<CustomerSummary>.Current.Writer);
        Assert.True(SqlEntityMap<CustomerSummary>.Current.ReadOnly);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_view_reads_and_pages_and_a_function_roots_a_chain(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);

        // The view: filtered, ordered, paged, projected; the writes refused with the reason.
        var expected = seed.Customers.Select(c => (c.Name, Orders: seed.Orders.Count(o => o.CustomerId == c.Id))).Where(c => c.Orders >= 10).OrderByDescending(c => c.Orders).ThenBy(c => c.Name, StringComparer.Ordinal).Skip(1).Take(5).ToList();
        var page = await session.Query<CustomerSummary>().Where((s, atLeast) => s.Orders >= atLeast, 10).OrderByDescending(s => s.Orders).ThenBy(s => s.Name).Skip(1).Take(5).Select(s => new { s.Name, s.Orders }).ToListAsync();
        Assert.Equal(expected, page.Select(p => (p.Name, p.Orders)));
        Assert.Equal(seed.Customers.Count, await session.Query<CustomerSummary>().CountAsync());
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => session.InsertAsync(new CustomerSummary { Id = Guid.NewGuid(), Name = "x" }));
        Assert.Contains("read-only", refused.Message, StringComparison.Ordinal);

        // The function (where the dialect has them: not SQLite, not MySQL): the customer's orders as the root, a navigation and an explicit join on them.
        if (dialect is SqlDialect.Sqlite or SqlDialect.MySql)
            return;
        var customer = seed.BusyCustomer;
        var rows = await session.Function<Order>("orders_of", customer.Id).Where(o => !o.IsDeleted).OrderBy(o => o.Number).Take(3).Select(o => new { o.Number, Who = o.Customer!.Name }).ToListAsync();
        Assert.Equal(seed.Orders.Where(o => o.CustomerId == customer.Id && !o.IsDeleted).OrderBy(o => o.Number, StringComparer.Ordinal).Take(3).Select(o => (o.Number, customer.Name)), rows.Select(r => (r.Number, r.Who)));
        var joined = await session.Function<Order>("orders_of", customer.Id).Join(session.Query<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => new { o.Number, c.Country }).OrderBy(x => x.Number).Take(2).ToListAsync();
        Assert.Equal(seed.Orders.Where(o => o.CustomerId == customer.Id).OrderBy(o => o.Number, StringComparer.Ordinal).Take(2).Select(o => (o.Number, customer.Country)), joined.Select(j => (j.Number, j.Country)));
        Assert.Equal(seed.Orders.Count(o => o.CustomerId == customer.Id), await session.Function<Order>("orders_of", customer.Id).CountAsync());
    }
}
