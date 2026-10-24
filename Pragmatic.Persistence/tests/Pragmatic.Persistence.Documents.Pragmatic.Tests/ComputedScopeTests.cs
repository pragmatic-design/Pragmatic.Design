using Pragmatic.Authorization;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Pragmatic;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Scopes;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     Pragmatic's computed scopes on MongoDB and Cosmos DB, as on the Sql engine (<c>ComputedScopeTests</c> there): a registered Computed rule
///     OR-ed into the scopes of a caller holding its scope; not registered, or the scope not held, it adds nothing; Admin lifts the group. The
///     documents: <see cref="DocumentRows" /> (RIVA acme desk-north, MARE acme desk-south, ALPE other).
/// </summary>
[Collection("document servers")]
public class ComputedScopeTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private DocSession As(DocSession seeded, DataScopeRule[] rules, params string[] scopes)
        => DocumentServers.As(seeded, new PragmaticDocFilters(new TestUser("anna"), new TenantScope(), new TestScopes(scopes), _toggle, scopeRules: rules));

    private static async Task<List<string>> Properties(DocSession session) => (await session.Query<Property>().ToListAsync()).Select(p => p.Name).Order(StringComparer.Ordinal).ToList();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_computed_rule_adds_its_documents_to_a_caller_holding_its_scope(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var seeded = await DocumentRows.SeededAsync(servers, store);
        Assert.Equal(["MARE", "RIVA"], await Properties(As(seeded, [new SeasidePropertiesRule()], "desk-north", ScopeIdentifiers.ForScope("seaside"))));
        Assert.Equal(["RIVA"], await Properties(As(seeded, [new SeasidePropertiesRule()], "desk-north"))); // the rule's scope not held
        Assert.Equal(["RIVA"], await Properties(As(seeded, [], "desk-north", ScopeIdentifiers.ForScope("seaside")))); // not registered
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_scope_groups_lifts_take_the_rule_with_them_and_a_materialized_rule_reads_nothing(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var seeded = await DocumentRows.SeededAsync(servers, store);
        var session = As(seeded, [new SeasidePropertiesRule(), new AlpinePropertiesRule()], ScopeIdentifiers.ForScope("seaside"), ScopeIdentifiers.ForScope("alpine"));
        Assert.Equal(["MARE"], await Properties(session));
        Assert.Equal(1, await session.Query<Property>().CountAsync());
        using (_toggle.UseMode(FilterMode.Admin))
            Assert.Equal(["MARE", "RIVA"], await Properties(session));
    }
}
