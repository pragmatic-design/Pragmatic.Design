using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A <c>[Sort]</c> with a <c>DefaultDirection</c> orders after the sorts the caller chose, whatever the declaration order, on the five
///     databases, as EF Core's <c>Apply</c> does (#642, #696).
/// </summary>
/// <remarks>
///     The engine applied the sorts by <c>Priority</c> alone, so <see cref="ReviewsByStarsQuery" />'s default, declared first, ordered first and
///     the caller's sort only broke its ties: the same query answered differently on EF Core.
/// </remarks>
[Collection("pragmatic databases")]
public class ADefaultSortComesAfterTheCallersTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<string[]> Texts(SqlDialect dialect, ReviewsByStarsQuery query)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        using (_toggle.UseMode(FilterMode.Raw))
            return [.. (await new Review.SqlReadRepository(session).RunAsync(query)).Select(r => r.Text)];
    }

    /// <summary>By text these read ALPE, MARE, RIVA, bruno's; by stars first, ALPE (2), bruno's (3), MARE (4), RIVA (5).</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_callers_sort_orders_first(SqlDialect dialect)
        => Assert.Equal(["anna@ALPE", "anna@MARE", "anna@RIVA", "bruno@RIVA"], await Texts(dialect, new ReviewsByStarsQuery { TextSort = SortDirection.Ascending }));

    /// <summary>The control: with no sort from the caller, the default alone orders.</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task With_no_sort_from_the_caller_the_default_orders(SqlDialect dialect)
        => Assert.Equal(["anna@ALPE", "bruno@RIVA", "anna@MARE", "anna@RIVA"], await Texts(dialect, new ReviewsByStarsQuery()));
}
