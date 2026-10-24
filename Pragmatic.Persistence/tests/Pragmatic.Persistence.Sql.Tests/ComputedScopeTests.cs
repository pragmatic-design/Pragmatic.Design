using Pragmatic.Authorization;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Scopes;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     Pragmatic's computed scopes on the five databases (<c>ComputedScopeFilter</c>): a registered <see cref="DataScopeRule{T}" />, Computed or
///     Hybrid, OR-ed into the scopes of a caller holding its scope (<c>scope:{ScopeName}</c>); not registered, or the scope not held, it adds
///     nothing; the scope group's lifts (Admin, the bypass permission) take it with them. The rows: <see cref="PragmaticRows" />.
/// </summary>
[Collection("pragmatic databases")]
public class ComputedScopeTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<SqlSession> Seeded(SqlDialect dialect, DataScopeRule[] rules, params string[] scopes)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes(scopes), _toggle, scopeRules: rules));
        await PragmaticRows.SeedAsync(session, _toggle);
        return session;
    }

    private static Task<List<string>> Properties(SqlSession session) => session.Query<Property>().OrderBy(p => p.Name).Select(p => p.Name).ToListAsync().AsTask();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_computed_rule_adds_its_rows_to_a_caller_holding_its_scope(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using (var holder = await Seeded(dialect, [new SeasidePropertiesRule()], "desk-north", ScopeIdentifiers.ForScope("seaside")))
            Assert.Equal(["MARE", "RIVA"], await Properties(holder)); // RIVA by the stored scope, MARE by the rule
        using (var other = await Seeded(dialect, [new SeasidePropertiesRule()], "desk-north"))
            Assert.Equal(["RIVA"], await Properties(other)); // the rule's scope not held: nothing added
        using (var unregistered = await Seeded(dialect, [], "desk-north", ScopeIdentifiers.ForScope("seaside")))
            Assert.Equal(["RIVA"], await Properties(unregistered)); // not registered: nothing added
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_scope_groups_lifts_take_the_rule_with_them_and_a_materialized_rule_reads_nothing(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect, [new SeasidePropertiesRule(), new AlpinePropertiesRule()], ScopeIdentifiers.ForScope("seaside"), ScopeIdentifiers.ForScope("alpine"));
        Assert.Equal(["MARE"], await Properties(session)); // the rule only: no stored scope held; the Materialized rule is the saves', not the reads'
        using (_toggle.UseMode(FilterMode.Admin))
            Assert.Equal(["MARE", "RIVA"], await Properties(session)); // the group lifted: the tenant's properties
        Assert.Equal(1, await session.Query<Property>().CountAsync((p, name) => p.Name == name, "MARE")); // in a count, with the chain's own condition
    }

    [Fact]
    public void A_registered_computed_rule_the_generator_did_not_compile_is_refused()
    {
        // Review has no scopes: no statement carries the rule, so reading it registered would read past it.
        var filters = new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes(), _toggle, scopeRules: [new OwnReviewsRule()]);
        var slot = new SqlFilterSlot(typeof(Review), SqlFilterFamily.Permission, typeof(Review.OwnershipFilter), null);
        var refused = Assert.Throws<NotSupportedException>(() => filters.StateOf(slot));
        Assert.Contains("OwnReviewsRule", refused.Message, StringComparison.Ordinal);
    }

    private sealed class OwnReviewsRule : DataScopeRule<Review>
    {
        public override string ScopeName => "reviews";

        public override ScopeStrategy Strategy => ScopeStrategy.Computed;

        public override System.Linq.Expressions.Expression<Func<Review, bool>> ToExpression() => r => r.Published;
    }
}
