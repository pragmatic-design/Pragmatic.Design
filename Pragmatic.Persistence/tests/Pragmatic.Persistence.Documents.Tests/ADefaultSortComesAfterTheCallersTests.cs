using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Filters;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A <c>[Sort]</c> with a <c>DefaultDirection</c> orders after the sorts the caller chose, whatever the declaration order, on MongoDB and
///     Cosmos DB, as EF Core's <c>Apply</c> does (#642, #696).
/// </summary>
/// <remarks>
///     The engine applied the sorts by <c>Priority</c> alone, so <see cref="ReviewsByStarsQuery" />'s default, declared first, ordered first and
///     the caller's sort only broke its ties: the same query answered differently on EF Core.
/// </remarks>
[Collection("document servers")]
public class ADefaultSortComesAfterTheCallersTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<string[]> Texts(Store store, ReviewsByStarsQuery query)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await DeclaredQueryTests.ReviewsAsync(servers, store, _toggle);
        using (_toggle.UseMode(FilterMode.Raw))
            return [.. (await new Review.DocReadRepository(session).RunAsync(query)).Select(r => r.Text)];
    }

    /// <summary>By text these read ALPE, MARE, RIVA, bruno's; by stars first, ALPE (2), bruno's (3), MARE (4), RIVA (5).</summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_callers_sort_orders_first(Store store)
        => Assert.Equal(["anna@ALPE", "anna@MARE", "anna@RIVA", "bruno@RIVA"], await Texts(store, new ReviewsByStarsQuery { TextSort = SortDirection.Ascending }));

    /// <summary>The control: with no sort from the caller, the default alone orders.</summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task With_no_sort_from_the_caller_the_default_orders(Store store)
        => Assert.Equal(["anna@ALPE", "bruno@RIVA", "anna@MARE", "anna@RIVA"], await Texts(store, new ReviewsByStarsQuery()));
}
