using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Common table expressions beyond the root: a recursive tree as a join's inner chain, and two of them in one statement (the root's and a
///     join's). Every WITH goes before the statement under its own name (SQL Server takes no WITH inside a derived table), RECURSIVE once for
///     the list where the dialect wants it. Rows checked against the tree built in memory. In a transaction, rolled back.
/// </summary>
[Collection("databases")]
public class CteJoinTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_tree_joins_and_two_withs_share_one_statement(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var mark = "ctej-" + Guid.NewGuid().ToString("N")[..6];
        var root = new Category { Id = Guid.NewGuid(), Name = mark + "-root" };
        var child = new Category { Id = Guid.NewGuid(), ParentId = root.Id, Name = mark + "-child" };
        var grand = new Category { Id = Guid.NewGuid(), ParentId = child.Id, Name = mark + "-grand" };
        var other = new Category { Id = Guid.NewGuid(), Name = mark + "-other" };
        await session.InsertManyAsync([root, child, grand, other]);

        // The tree under the root (its descendants) as a join's inner chain: each category with the descendants it is the parent of.
        var pairs = await session.Query<Category>()
            .Join(session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root.Id), (c, tree) => c.ParentId == tree.Id), c => c.Id, d => d.ParentId, (c, d) => new { Parent = c.Name, Child = d.Name })
            .ToListAsync();
        Assert.Equal([(child.Name, grand.Name), (root.Name, child.Name)], pairs.Select(p => (p.Parent, p.Child)).OrderBy(p => p.Parent, StringComparer.Ordinal));

        // Two WITHs: the tree as the root, the categories with this mark as a plain one joined to it.
        var named = await session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root.Id), (c, tree) => c.ParentId == tree.Id)
            .Join(session.With(session.Query<Category>().Where((c, m) => Sql.Like(c.Name, m), mark + "%")), d => d.Id, m => m.Id, (d, m) => m.Name)
            .ToListAsync();
        Assert.Equal([child.Name, grand.Name], named.Order(StringComparer.Ordinal));

        var sql = session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root.Id), (c, tree) => c.ParentId == tree.Id)
            .Join(session.With(session.Query<Category>().Where((c, m) => Sql.Like(c.Name, m), mark + "%")), d => d.Id, m => m.Id, (d, m) => m.Name).ToSql();
        Assert.True(sql.StartsWith("WITH ", StringComparison.Ordinal) && sql.IndexOf("WITH ", 1, StringComparison.Ordinal) < 0, sql); // one WITH list, at the head
        transaction.Rollback();
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_bounded_recursive_with_stops_at_its_depth_even_on_a_cycle(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var mark = "cted-" + Guid.NewGuid().ToString("N")[..6];
        var x = new Category { Id = Guid.NewGuid(), Name = mark + "-x" };
        var y = new Category { Id = Guid.NewGuid(), ParentId = x.Id, Name = mark + "-y" };
        await session.InsertManyAsync([x, y]);
        x.ParentId = y.Id; // x → y → x: an unbounded walk down would not end
        await session.UpdateAsync(x);

        // Depth 0 is the anchor (x); each step one level more: x, y, x, y at depths 0 to 3.
        var walked = await session.With(session.Query<Category>().Where((c, id) => c.Id == id, x.Id), (c, tree) => c.ParentId == tree.Id, 3).Select(c => c.Name).ToListAsync();
        Assert.Equal([x.Name, x.Name, y.Name, y.Name], walked.Order(StringComparer.Ordinal));
        Assert.Equal([x.Name], await session.With(session.Query<Category>().Where((c, id) => c.Id == id, x.Id), (c, tree) => c.ParentId == tree.Id, 0).Select(c => c.Name).ToListAsync());
        transaction.Rollback();
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_walk_stops_at_cycles_and_orders_depth_or_breadth_first(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var mark = "ctew-" + Guid.NewGuid().ToString("N")[..6];

        // A cycle x → y → x: StopAtCycles ends the walk at the row already on the path.
        var x = new Category { Id = Guid.NewGuid(), Name = mark + "-x" };
        var y = new Category { Id = Guid.NewGuid(), ParentId = x.Id, Name = mark + "-y" };
        await session.InsertManyAsync([x, y]);
        x.ParentId = y.Id;
        await session.UpdateAsync(x);
        var walked = await session.With(session.Query<Category>().Where((c, id) => c.Id == id, x.Id), (c, tree) => c.ParentId == tree.Id, SqlTreeWalk.StopAtCycles).Select(c => c.Name).ToListAsync();
        Assert.Equal([x.Name, y.Name], walked.Order(StringComparer.Ordinal));

        // A tree whose keys start with 1, 2, 3, 4: the path's text orders the same under every collation.
        static Guid Key(char first) => Guid.Parse(first + Guid.NewGuid().ToString("N")[1..]);
        var root = new Category { Id = Key('1'), Name = mark + "-root" };
        var a = new Category { Id = Key('2'), ParentId = root.Id, Name = mark + "-a" };
        var b = new Category { Id = Key('3'), ParentId = root.Id, Name = mark + "-b" };
        var a1 = new Category { Id = Key('4'), ParentId = a.Id, Name = mark + "-a1" };
        await session.InsertManyAsync([root, a, b, a1]);
        var depthFirst = await session.With(session.Query<Category>().Where((c, id) => c.Id == id, root.Id), (c, tree) => c.ParentId == tree.Id, SqlTreeWalk.DepthFirst).Select(c => c.Name).ToListAsync();
        Assert.Equal([root.Name, a.Name, a1.Name, b.Name], depthFirst);
        var breadthFirst = await session.With(session.Query<Category>().Where((c, id) => c.Id == id, root.Id), (c, tree) => c.ParentId == tree.Id, SqlTreeWalk.BreadthFirst | SqlTreeWalk.StopAtCycles, maxDepth: 5).Select(c => c.Name).ToListAsync();
        Assert.Equal([root.Name, a.Name, b.Name, a1.Name], breadthFirst);
        transaction.Rollback();
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_step_reads_a_navigation_of_the_tables_row(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var mark = "cten-" + Guid.NewGuid().ToString("N")[..6];
        var root = new Category { Id = Guid.NewGuid(), Name = mark + "-root" };
        var a = new Category { Id = Guid.NewGuid(), ParentId = root.Id, Name = mark + "-a-go" };
        var b = new Category { Id = Guid.NewGuid(), ParentId = root.Id, Name = mark + "-b-stop" };
        var a1 = new Category { Id = Guid.NewGuid(), ParentId = a.Id, Name = mark + "-a1" };
        var b1 = new Category { Id = Guid.NewGuid(), ParentId = b.Id, Name = mark + "-b1" };
        await session.InsertManyAsync([root, a, b, a1, b1]);

        // Down from the root's children, only below a parent whose name says go: the step reads c.Parent (an INNER JOIN in the recursive member).
        var walked = await session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root.Id), (c, tree) => c.ParentId == tree.Id && Sql.Like(c.Parent!.Name, "%-go")).Select(c => c.Name).ToListAsync();
        Assert.Equal([a.Name, a1.Name, b.Name], walked.Order(StringComparer.Ordinal));

        // A navigation of the tree's row: its joins after the tree's, the step's predicate after them.
        var underNamed = await session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root.Id), (c, tree) => c.ParentId == tree.Id && Sql.Like(tree.Parent!.Name, "cten-%")).Select(c => c.Name).ToListAsync();
        Assert.Equal([a.Name, a1.Name, b.Name, b1.Name], underNamed.Order(StringComparer.Ordinal));
        transaction.Rollback();
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Withs_are_members_of_a_set_operation(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var mark = "ctes-" + Guid.NewGuid().ToString("N")[..6];
        var root1 = new Category { Id = Guid.NewGuid(), Name = mark + "-r1" };
        var c1 = new Category { Id = Guid.NewGuid(), ParentId = root1.Id, Name = mark + "-c1" };
        var g1 = new Category { Id = Guid.NewGuid(), ParentId = c1.Id, Name = mark + "-g1" };
        var root2 = new Category { Id = Guid.NewGuid(), Name = mark + "-r2" };
        var c2 = new Category { Id = Guid.NewGuid(), ParentId = root2.Id, Name = mark + "-c2" };
        await session.InsertManyAsync([root1, c1, g1, root2, c2]);

        // Two trees as the members of a UNION: both defined in the statement's WITH list, each member reading its own by name.
        var both = await session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root1.Id), (c, tree) => c.ParentId == tree.Id)
            .Union(session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root2.Id), (c, tree) => c.ParentId == tree.Id))
            .ToListAsync();
        Assert.Equal([c1.Name, c2.Name, g1.Name], both.Select(c => c.Name).Order(StringComparer.Ordinal));

        // A plain member and a tree: the parameters of each member stay theirs.
        var mixed = await session.Query<Category>().Where((c, id) => c.Id == id, root1.Id)
            .Concat(session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root2.Id), (c, tree) => c.ParentId == tree.Id))
            .ToListAsync();
        Assert.Equal([c2.Name, root1.Name], mixed.Select(c => c.Name).Order(StringComparer.Ordinal));
        transaction.Rollback();
    }
}
