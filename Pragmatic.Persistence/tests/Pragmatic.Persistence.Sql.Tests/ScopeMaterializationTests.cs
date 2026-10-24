using Pragmatic.Authorization;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The data scope rules on the rows a save writes (Pragmatic's <c>ScopeMaterializer</c>, run by <c>ScopeInterceptor</c> after the creator's
///     scope), on the five databases: a Materialized rule adds its scope to a row it matches and takes it from one it no longer matches; a
///     caller holding the scope then reads the row through its stored scopes.
/// </summary>
[Collection("pragmatic databases")]
public class ScopeMaterializationTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_materialized_rule_writes_its_scope_on_the_rows_it_matches_and_takes_it_back(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var alpine = ScopeIdentifiers.ForScope("alpine");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes(alpine), _toggle));
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope(), scopeRules: [new AlpinePropertiesRule(), new SeasidePropertiesRule()]);
        var alba = new Property { Name = "ALBA" };
        new Property.SqlRepository(unitOfWork).Add(alba);
        await unitOfWork.SaveChangesAsync();
        Assert.Equal([ScopeIdentifiers.ForUser("anna"), alpine], alba.AccessScopes); // the creator's first, as ScopeInterceptor stamps it before its steps

        // The caller holds "scope:alpine" only: the row is hers to read through the scope the save stored (the Computed rule wrote nothing).
        Assert.Equal(["ALBA"], await session.Query<Property>().Select(p => p.Name).ToListAsync());

        alba.Name = "BRUNA"; // no longer the rule's
        await unitOfWork.SaveChangesAsync();
        using (_toggle.UseMode(FilterMode.Raw))
            Assert.Equal([ScopeIdentifiers.ForUser("anna")], (await session.Query<Property>().FirstOrDefaultAsync())!.AccessScopes);
        Assert.Empty(await session.Query<Property>().ToListAsync());
    }
}
