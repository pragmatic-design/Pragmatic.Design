using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     Pragmatic's <c>[GenerateHierarchy]</c> on the five databases: the descendants and the ancestors along the tree's parent key, the starting
///     row left out, the walk unfiltered and the rows read within the caller's filters (a deleted node is not returned, its children are), as
///     Pragmatic's CTE and EF Core's filters on its result do. The tree: ROOT, A and B under it, A1 and A2 under A (A2 deleted), A1x under A1.
/// </summary>
[Collection("pragmatic databases")]
public class HierarchyTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<(SqlSession Session, Dictionary<string, Category> Nodes)> Seeded(SqlDialect dialect)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes(), _toggle));
        var nodes = new Dictionary<string, Category>();
        using (_toggle.UseMode(FilterMode.Raw))
        {
            foreach (var (name, parent, deleted) in new (string, string?, bool)[] { ("ROOT", null, false), ("A", "ROOT", false), ("B", "ROOT", false), ("A1", "A", false), ("A2", "A", true), ("A1x", "A1", false) })
            {
                var node = new Category { Name = name, IsDeleted = deleted };
                if (parent is not null)
                    node.SetParentId(nodes[parent].Id);
                nodes[name] = node;
                await session.InsertAsync(node);
            }
        }

        return (session, nodes);
    }

    private static List<string> Names(IEnumerable<Category> rows) => rows.Select(c => c.Name).Order(StringComparer.Ordinal).ToList();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_descendants_and_ancestors_are_read_within_the_callers_filters(SqlDialect dialect)
    {
        var (session, nodes) = await Seeded(dialect);
        using (session)
        {
            Assert.Equal(["A", "A1", "A1x", "B"], Names(await Category.GetDescendantsByParentAsync(session, nodes["ROOT"].Id))); // A2 deleted: not returned
            Assert.Equal(["A1", "A1x"], Names(await Category.GetDescendantsByParentAsync(session, nodes["A"].Id)));
            Assert.Equal(["A", "A1", "ROOT"], Names(await Category.GetAncestorsByParentAsync(session, nodes["A1x"].Id)));
            Assert.Empty(await Category.GetAncestorsByParentAsync(session, nodes["ROOT"].Id));
            using (_toggle.UseMode(FilterMode.Raw))
                Assert.Equal(["A1", "A1x", "A2"], Names(await Category.GetDescendantsByParentAsync(session, nodes["A"].Id))); // Raw: the deleted one too
        }
    }
}
