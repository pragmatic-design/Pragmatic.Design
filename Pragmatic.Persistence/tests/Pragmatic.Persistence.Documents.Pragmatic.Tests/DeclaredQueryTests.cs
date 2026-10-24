using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     Pragmatic's declared queries on the documents engine, through <c>IReadRepository.RunAsync</c> as Pragmatic's generated invokers call it,
///     on MongoDB and Cosmos DB: the Sql engine's declarations and expectations (<c>DeclaredQueryTests</c> there). In Raw mode the tenant is
///     lifted, so on Cosmos DB the page is read across partitions, range by range, and merged.
/// </summary>
[Collection("document servers")]
public class DeclaredQueryTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    /// <summary>
    ///     The Sql engine's review rows (<c>PragmaticRows</c>), all of the tenant acme: anna's at RIVA (5 stars) and MARE (4), bruno's at RIVA (3),
    ///     anna's unpublished at ALPE (2). The session is anna's (acme, desk-north).
    /// </summary>
    internal static async Task<(DocSession Session, List<Review> All)> ReviewsAsync(DocumentServers servers, Store store, IQueryFilterToggle toggle)
    {
        var seeding = servers.Session(store);
        if (store == Store.Cosmos)
            await seeding.EnsureContainerAsync<Review>();
        var properties = new Dictionary<string, Guid> { ["RIVA"] = Guid.NewGuid(), ["MARE"] = Guid.NewGuid(), ["ALPE"] = Guid.NewGuid() };
        var reviews = new List<Review>();
        foreach (var (owner, at, published, stars) in new[] { ("anna", "RIVA", true, 5), ("bruno", "RIVA", true, 3), ("anna", "MARE", true, 4), ("anna", "ALPE", false, 2) })
        {
            var review = new Review { Text = owner + "@" + at, Published = published, Stars = stars, TenantId = "acme" };
            review.SetOwnerId(owner);
            review.SetPropertyId(properties[at]);
            reviews.Add(review);
        }

        await seeding.InsertManyAsync(reviews);
        return (DocumentServers.As(seeding, DocumentRows.Anna(toggle)), reviews);
    }

    /// <summary>The page's texts and total, as one string.</summary>
    private static async Task<string> Run(IReadRepository<Review> reviews, SearchReviewsQuery query)
    {
        var page = await reviews.RunAsync(query);
        return Page([.. page.Items.Select(r => r.Text)], page.TotalCount);
    }

    private static string Page(string[] texts, int total) => $"[{string.Join(", ", texts)}] of {total}";

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_declared_query_filters_sorts_and_pages_as_declared(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, all) = await ReviewsAsync(servers, store, _toggle);
        IReadRepository<Review> reviews = new Review.DocReadRepository(session);
        using (_toggle.UseMode(FilterMode.Raw)) // the query's own semantics, no family in the way
        {
            Assert.Equal(Page(["bruno@RIVA", "anna@RIVA"], 4), await Run(reviews, new SearchReviewsQuery())); // the default sort, descending; the first page of two
            Assert.Equal(Page(["anna@MARE", "anna@ALPE"], 4), await Run(reviews, new SearchReviewsQuery { Page = 2 }));
            Assert.Equal(Page(["anna@ALPE", "anna@MARE"], 4), await Run(reviews, new SearchReviewsQuery { TextSort = SortDirection.Ascending }));
            Assert.Equal(Page(["bruno@RIVA", "anna@RIVA"], 2), await Run(reviews, new SearchReviewsQuery { Text = "RIVA" })); // Contains
            Assert.Equal(Page(["anna@ALPE"], 1), await Run(reviews, new SearchReviewsQuery { Published = false }));
            Assert.Equal(Page(["anna@RIVA", "anna@MARE"], 3), await Run(reviews, new SearchReviewsQuery { Prefix = "ANNA@" })); // StartsWith, ignoring case
            var riva = all.First(r => r.Text == "anna@RIVA").PropertyId;
            Assert.Equal(Page(["bruno@RIVA", "anna@RIVA"], 2), await Run(reviews, new SearchReviewsQuery { Properties = [riva] })); // In
            Assert.Equal(Page(["bruno@RIVA", "anna@RIVA"], 4), await Run(reviews, new SearchReviewsQuery { Properties = [] })); // an empty collection is no filter
        }
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_declared_query_reads_inside_the_callers_filters_and_projects(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await ReviewsAsync(servers, store, _toggle);
        IReadRepository<Review> reviews = new Review.DocReadRepository(session);
        Assert.Equal(Page(["anna@RIVA", "anna@MARE"], 2), await Run(reviews, new SearchReviewsQuery())); // anna's published ones
        var lines = await reviews.RunAsync(new ReviewLinesQuery { TextSort = SortDirection.Ascending });
        Assert.Equal(["anna@MARE", "anna@RIVA"], lines.Select(l => l.Text));
        Assert.All(lines, l => Assert.True(l.Published));
        Assert.Equal(["anna@RIVA"], (await reviews.RunAsync(new ReviewLinesQuery { Text = "anna@RIVA" })).Select(l => l.Text));
    }

    [Fact]
    public void ToSpecification_is_the_filters_Pragmatic_composes()
    {
        var specification = new SearchReviewsQuery { Text = "RIVA", Published = true }.ToSpecification();
        var reviews = new[] { new Review { Text = "anna@RIVA", Published = true }, new Review { Text = "anna@RIVA", Published = false }, new Review { Text = "anna@MARE", Published = true } };
        var matches = specification.ToExpression().Compile();
        Assert.Single(reviews, r => matches(r));
    }
}
