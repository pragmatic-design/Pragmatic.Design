using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Pragmatic;
using Pragmatic.Persistence.Query.Filters;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     Pragmatic's <c>[GenerateHierarchy]</c> on MongoDB (<c>$graphLookup</c>) and Cosmos DB (a query per level), as on the Sql engine
///     (<c>HierarchyTests</c> there): the descendants and the ancestors, the start left out, the walk unfiltered, the documents read within the
///     caller's filters. The tree: ROOT, A and B under it, A1 and A2 under A (A2 deleted), A1x under A1.
/// </summary>
[Collection("document servers")]
public class HierarchyTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<(DocSession Session, Dictionary<string, Category> Nodes)> Seeded(Store store)
    {
        var seeding = servers.Session(store);
        if (store == Store.Cosmos)
            await seeding.EnsureContainerAsync<Category>();
        var nodes = new Dictionary<string, Category>();
        foreach (var (name, parent, deleted) in new (string, string?, bool)[] { ("ROOT", null, false), ("A", "ROOT", false), ("B", "ROOT", false), ("A1", "A", false), ("A2", "A", true), ("A1x", "A1", false) })
        {
            var node = new Category { Name = name, IsDeleted = deleted };
            if (parent is not null)
                node.SetParentId(nodes[parent].Id);
            nodes[name] = node;
        }

        await seeding.InsertManyAsync(nodes.Values.ToList());
        return (DocumentServers.As(seeding, new PragmaticDocFilters(new TestUser("anna"), new TenantScope(), new TestScopes(), _toggle)), nodes);
    }

    private static List<string> Names(IEnumerable<Category> rows) => rows.Select(c => c.Name).Order(StringComparer.Ordinal).ToList();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_descendants_and_ancestors_are_read_within_the_callers_filters(Store store)
    {
        var (session, nodes) = await Seeded(store);
        Assert.Equal(["A", "A1", "A1x", "B"], Names(await Category.GetDescendantsByParentAsync(session, nodes["ROOT"].Id)));
        Assert.Equal(["A1", "A1x"], Names(await Category.GetDescendantsByParentAsync(session, nodes["A"].Id)));
        Assert.Equal(["A", "A1", "ROOT"], Names(await Category.GetAncestorsByParentAsync(session, nodes["A1x"].Id)));
        Assert.Empty(await Category.GetAncestorsByParentAsync(session, nodes["ROOT"].Id));
        using (_toggle.UseMode(FilterMode.Raw))
            Assert.Equal(["A1", "A1x", "A2"], Names(await Category.GetDescendantsByParentAsync(session, nodes["A"].Id)));
    }
}
