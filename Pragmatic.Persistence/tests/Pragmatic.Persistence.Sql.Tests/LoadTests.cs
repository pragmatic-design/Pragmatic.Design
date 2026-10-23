using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     An Include whose collection is ordered (<c>c.Orders.OrderBy(…)</c>) and taken per parent (<c>.Take(n)</c>: a window partitioned by the
///     foreign key, in the companion statement), on the typed plan and on the runtime path; and explicit loading of a collection, a
///     many-to-many side and a to-one navigation on rows already in hand (<c>session.LoadAsync</c>), compared with the seed.
/// </summary>
[Collection("databases")]
public class LoadTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void An_ordered_and_taken_include_renders_as_a_window_per_parent()
    {
        var session = Session(SqlDialect.PostgreSql);
        var ordered = session.Query<Customer>().Where(c => c.Country == "IT").Include(c => c.Orders.Where(o => !o.IsDeleted).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Number)).ToSql();
        Assert.EndsWith(" FROM \"Orders\" t WHERE (t.\"CustomerId\" IN (SELECT t.\"Id\" FROM \"Customers\" t WHERE (t.\"Country\" = @p0))) AND (NOT (t.\"IsDeleted\" = @p1)) ORDER BY t.\"CreatedAt\" DESC, t.\"Number\"", ordered, StringComparison.Ordinal);

        var taken = session.Query<Customer>().Where(c => c.Country == "IT").Include(c => c.Orders.OrderByDescending(o => o.CreatedAt).Take(2)).ToSql();
        Assert.Contains("; SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM (SELECT t.*, ROW_NUMBER() OVER (PARTITION BY t.\"CustomerId\" ORDER BY t.\"CreatedAt\" DESC) AS rn FROM \"Orders\" t WHERE (t.\"CustomerId\" IN (SELECT t.\"Id\" FROM \"Customers\" t WHERE (t.\"Country\" = @p0)))) t WHERE (t.rn <= 2) ORDER BY t.\"CustomerId\", t.rn", taken, StringComparison.Ordinal);

        // The runtime path renders the same companion.
        var runtime = session.Query<Customer>().AsQueryable().Where(c => c.Country == "IT").Include(c => c.Orders.OrderByDescending(o => o.CreatedAt).Take(2)).ToSql();
        Assert.Equal(taken, runtime);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task The_children_come_ordered_and_taken_per_parent(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var customers = seed.Customers.OrderBy(c => c.Id).Take(5).ToList();
        var ids = customers.Select(c => c.Id).ToArray();

        // Ordered: every order of the customer, newest first, the number as the tie-break.
        var ordered = await session.Query<Customer>().Where((c, keys) => keys.Contains(c.Id), ids).Include(c => c.Orders.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Number)).OrderBy(c => c.Id).ToListAsync();
        Assert.Equal(5, ordered.Count);
        foreach (var customer in ordered)
        {
            var expected = seed.Orders.Where(o => o.CustomerId == customer.Id).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Number, StringComparer.Ordinal).Select(o => o.Id).ToList();
            Assert.Equal(expected, customer.Orders.Select(o => o.Id));
        }

        // Taken: the two newest live orders per customer, in one statement; the same through the runtime path and under a nested include.
        var taken = await session.Query<Customer>().Where((c, keys) => keys.Contains(c.Id), ids).Include(c => c.Orders.Where(o => !o.IsDeleted).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Number).Take(2).Then(o => o.Lines)).OrderBy(c => c.Id).ToListAsync();
        var runtime = await session.Query<Customer>().AsQueryable().Where(c => ids.Contains(c.Id)).Include(c => c.Orders.Where(o => !o.IsDeleted).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Number).Take(2)).OrderBy(c => c.Id).ToListAsync();
        foreach (var (customer, dynamic) in taken.Zip(runtime))
        {
            var expected = seed.Orders.Where(o => o.CustomerId == customer.Id && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Number, StringComparer.Ordinal).Take(2).Select(o => o.Id).ToList();
            Assert.Equal(expected, customer.Orders.Select(o => o.Id));
            Assert.Equal(expected, dynamic.Orders.Select(o => o.Id));
            Assert.All(customer.Orders, o => Assert.Equal(Seed.LinesPerOrder, o.Lines.Count));
        }
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Explicit_loading_fills_collections_links_and_navigations_of_rows_in_hand(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var orders = await session.Query<Order>().Where(o => !o.IsDeleted).OrderBy(o => o.Id).Take(7).ToListAsync();
        Assert.All(orders, o => Assert.Empty(o.Lines));
        Assert.All(orders, o => Assert.Null(o.Customer));

        // A collection of many rows at once (one statement), a navigation of many rows (one statement), one row alone.
        await session.LoadAsync(orders, o => o.Lines);
        await session.LoadAsync(orders, o => o.Customer);
        foreach (var order in orders)
        {
            Assert.Equal(seed.Lines.Where(l => l.OrderId == order.Id).Select(l => l.Id).OrderBy(id => id), order.Lines.Select(l => l.Id).OrderBy(id => id));
            Assert.Equal(order.CustomerId, order.Customer!.Id);
        }

        var one = await session.Query<Customer>().OrderBy(c => c.Id).FirstAsync();
        await session.LoadAsync(one, c => c.Orders);
        Assert.Equal(seed.Orders.Count(o => o.CustomerId == one.Id), one.Orders.Count);

        // A many-to-many side through its join table, and a parent with no children or no target.
        var labels = new[] { new Label { Id = Guid.NewGuid(), Name = "a" }, new Label { Id = Guid.NewGuid(), Name = "b" } };
        var project = new Project { Id = Guid.NewGuid(), Name = "load" };
        var empty = new Project { Id = Guid.NewGuid(), Name = "empty" };
        try
        {
            await session.InsertManyAsync(labels);
            await session.InsertManyAsync([project, empty]);
            await session.WriteBatch().Link(project, nameof(Project.Labels), labels[0]).Link(project, nameof(Project.Labels), labels[1]).ExecuteAsync();
            var projects = await session.Query<Project>().Where((p, keys) => keys.Contains(p.Id), new[] { project.Id, empty.Id }).OrderBy(p => p.Name).ToListAsync();
            await session.LoadAsync(projects, p => p.Labels);
            Assert.Equal(["a", "b"], projects.Single(p => p.Id == project.Id).Labels.Select(l => l.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Empty(projects.Single(p => p.Id == empty.Id).Labels);
            await session.LoadAsync(projects, p => p.Tickets);
            Assert.All(projects, p => Assert.Empty(p.Tickets));
            var refused = await Assert.ThrowsAsync<ArgumentException>(() => session.LoadAsync(project, p => p.Name));
            Assert.Contains("not a navigation or a collection", refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            await session.DeleteGraphAsync(project);
            await session.DeleteGraphAsync(empty);
            await session.Query<Label>().Where((l, keys) => keys.Contains(l.Id), labels.Select(l => l.Id).ToArray()).ExecuteDeleteAsync();
        }
    }
}
