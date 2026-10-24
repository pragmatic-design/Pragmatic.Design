using Pragmatic.Authorization;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The data scope rules on the documents a save writes, as on the Sql engine (<c>ScopeMaterializationTests</c> there): a Materialized rule
///     adds its scope to a document it matches and takes it from one it no longer matches; a caller holding the scope reads the document.
/// </summary>
[Collection("document servers")]
public class ScopeMaterializationTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_materialized_rule_writes_its_scope_on_the_documents_it_matches_and_takes_it_back(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var alpine = ScopeIdentifiers.ForScope("alpine");
        var seeding = servers.Session(store);
        if (store == Store.Cosmos)
            await seeding.EnsureContainerAsync<Property>();
        var session = DocumentServers.As(seeding, new PragmaticDocFilters(new TestUser("anna"), new TenantScope(), new TestScopes(alpine), _toggle));
        var unitOfWork = new DocUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope(), scopeRules: [new AlpinePropertiesRule(), new SeasidePropertiesRule()]);
        var alba = new Property { Name = "ALBA" };
        new Property.DocRepository(unitOfWork).Add(alba);
        await unitOfWork.SaveChangesAsync();
        Assert.Equal([ScopeIdentifiers.ForUser("anna"), alpine], alba.AccessScopes);
        Assert.Equal(["ALBA"], (await session.Query<Property>().ToListAsync()).Select(p => p.Name));

        alba.Name = "BRUNA";
        await unitOfWork.SaveChangesAsync();
        using (_toggle.UseMode(FilterMode.Raw))
            Assert.Equal([ScopeIdentifiers.ForUser("anna")], (await session.Query<Property>().ToListAsync()).Single().AccessScopes);
        Assert.Empty(await session.Query<Property>().ToListAsync());
    }
}
