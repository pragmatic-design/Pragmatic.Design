using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Common table expressions as the root of a chain: a plain one (<c>WITH cte AS (query)</c>, MATERIALIZED where the dialect has it)
///     and a recursive one (<c>WITH RECURSIVE tree AS (anchor UNION ALL step)</c>), the rest of the chain on the rows; the generated
///     <c>[GenerateHierarchy]</c> walks are the same statement, written by hand with <c>With</c>.
/// </summary>
[Collection("databases")]
public class CteTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    private const string Columns = "t.\"Id\", t.\"ParentId\", t.\"Name\"";

    [Fact]
    public void With_renders_a_plain_and_a_recursive_cte_per_dialect()
    {
        var key = Guid.NewGuid();
        // The recursive walk: the anchor's slot first, the outer filter's after; the same text [GenerateHierarchy] emits (which binds the key once).
        var descendants = Session(SqlDialect.PostgreSql).With(Session(SqlDialect.PostgreSql).Query<Category>().Where((c, id) => c.ParentId == id, key), (c, tree) => c.ParentId == tree.Id).Where((c, id) => c.Id != id, key).ToSql();
        Assert.Equal($"WITH RECURSIVE tree AS (SELECT {Columns} FROM \"Categories\" t WHERE (t.\"ParentId\" = @p0) UNION ALL SELECT {Columns} FROM \"Categories\" t INNER JOIN tree ON (t.\"ParentId\" = tree.\"Id\")) SELECT {Columns} FROM tree t WHERE (t.\"Id\" <> @p1)", descendants);
        var ancestors = Session(SqlDialect.SqlServer).With(Session(SqlDialect.SqlServer).Query<Category>().Where((c, id) => c.Id == id, key), (c, tree) => c.Id == tree.ParentId).Where((c, id) => c.Id != id, key).OrderBy(c => c.Name).ToSql();
        Assert.Equal("WITH tree AS (SELECT t.[Id], t.[ParentId], t.[Name] FROM [Categories] t WHERE (t.[Id] = @p0) UNION ALL SELECT t.[Id], t.[ParentId], t.[Name] FROM [Categories] t INNER JOIN tree ON (t.[Id] = tree.[ParentId])) SELECT t.[Id], t.[ParentId], t.[Name] FROM tree t WHERE (t.[Id] <> @p1) ORDER BY t.[Name]", ancestors);

        // A plain CTE, materialized where the dialect knows the word; a count over the tree.
        var materialized = Session(SqlDialect.PostgreSql).With(Session(SqlDialect.PostgreSql).Query<Order>().Where(o => !o.IsDeleted), materialized: true).OrderBy(o => o.Number).Take(3).Select(o => new { o.Number }).ToSql();
        Assert.Equal("WITH cte AS MATERIALIZED (SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM \"Orders\" t WHERE (NOT (t.\"IsDeleted\" = @p0))) SELECT t.\"Number\" FROM cte t ORDER BY t.\"Number\" LIMIT @p1", materialized);
        Assert.StartsWith("WITH cte AS (SELECT ", Session(SqlDialect.SqlServer).With(Session(SqlDialect.SqlServer).Query<Order>().Where(o => !o.IsDeleted), materialized: true).ToSql(), StringComparison.Ordinal); // no MATERIALIZED there (PRAG3012)
        Assert.StartsWith("WITH cte AS MATERIALIZED (SELECT ", Session(SqlDialect.Sqlite).With(Session(SqlDialect.Sqlite).Query<Order>().Where(o => !o.IsDeleted), materialized: true).ToSql(), StringComparison.Ordinal);

        // The entity's visibility rules go into the anchor.
        var live = Session(SqlDialect.PostgreSql).With(Session(SqlDialect.PostgreSql).Query<LiveOrder>().Where((o, id) => o.CustomerId == id, key)).ToSql();
        Assert.Contains("FROM \"Orders\" t WHERE (t.\"CustomerId\" = @p0) AND (NOT (t.\"IsDeleted\" = @p1))) SELECT ", live, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task With_walks_a_tree_as_the_generated_hierarchy_does(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var root = new Category { Id = Guid.NewGuid(), Name = "cte-root" };
        var children = Enumerable.Range(0, 2).Select(i => new Category { Id = Guid.NewGuid(), ParentId = root.Id, Name = "cte-child-" + i }).ToList();
        var grandchildren = children.SelectMany((c, i) => Enumerable.Range(0, i + 2).Select(j => new Category { Id = Guid.NewGuid(), ParentId = c.Id, Name = $"cte-grand-{i}-{j}" })).ToList();
        await session.InsertManyAsync([root, .. children, .. grandchildren]);

        // Descendants and ancestors by hand, against the generated walks: the same rows.
        var descendants = await session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root.Id), (c, tree) => c.ParentId == tree.Id).Where((c, id) => c.Id != id, root.Id).OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
        Assert.Equal((await session.GetDescendantsByParent(root.Id)).Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal), descendants);
        Assert.Equal(children.Count + grandchildren.Count, descendants.Count);
        var leaf = grandchildren[^1];
        var ancestors = await session.With(session.Query<Category>().Where((c, id) => c.Id == id, leaf.Id), (c, tree) => c.Id == tree.ParentId).Where((c, id) => c.Id != id, leaf.Id).OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
        Assert.Equal((await session.GetAncestorsByParent(leaf.Id)).Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal), ancestors);
        Assert.Equal(["cte-child-1", "cte-root"], ancestors);

        // The chain goes on from the tree: a count, a filter with its own value, a page.
        Assert.Equal(grandchildren.Count, await session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root.Id), (c, tree) => c.ParentId == tree.Id).CountAsync((c, prefix) => c.Name.StartsWith(prefix), "cte-grand-"));
        var page = await session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root.Id), (c, tree) => c.ParentId == tree.Id).OrderBy(c => c.Name).Skip(1).Take(2).Select(c => c.Name).ToListAsync();
        Assert.Equal(children.Concat(grandchildren).Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).Skip(1).Take(2), page);

        // A plain CTE: the same rows as the query itself.
        var plain = await session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root.Id), materialized: true).OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
        Assert.Equal(children.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal), plain);
    }
}
