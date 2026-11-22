using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching;
using Pragmatic.Caching.Extensions;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A <c>[Cacheable]</c> query on the documents engine reads through its cache (#882), and an owned entity's answer is the caller's own:
///     two callers with different documents never share an entry, as EF Core's executor partitions it.
/// </summary>
/// <remarks>
///     One cache for both callers, as a host has one: what keeps them apart is the key. Each caller reads through a session filtered for them,
///     so a shared entry would hand the second caller the first caller's reviews.
/// </remarks>
[Collection("document servers")]
public class ACachedQueryIsTheCallersOwnTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Two_owners_reading_the_same_cached_query_each_get_their_own_documents(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var cache = Cache();
        var (anna, _) = await DeclaredQueryTests.ReviewsAsync(servers, store, _toggle);
        var bruno = DocumentServers.As(anna, new PragmaticDocFilters(new TestUser("bruno"), new TenantScope(), new TestScopes("desk-north"), _toggle));

        var hers = await Read(cache, anna, "anna");
        var his = await Read(cache, bruno, "bruno");

        Assert.Equal(["anna@MARE", "anna@RIVA"], hers); // anna@ALPE is unpublished: [VisibleWhen<PublishedReviews>] hides it
        Assert.Equal(["bruno@RIVA"], his);
    }

    /// <summary>The control: the same caller twice is served the entry, a document added in between is not seen until it expires.</summary>
    /// <remarks>Without it the case above holds for a query that reads the store every time, cached or not.</remarks>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_same_caller_is_served_the_cached_answer(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var cache = Cache();
        var (anna, all) = await DeclaredQueryTests.ReviewsAsync(servers, store, _toggle);

        var first = await Read(cache, anna, "anna");
        var review = new Review { Text = "anna@LAGO", Published = true, Stars = 1, TenantId = "acme" };
        review.SetOwnerId("anna");
        review.SetPropertyId(all[0].PropertyId);
        await anna.InsertAsync(review);
        var second = await Read(cache, anna, "anna");

        Assert.DoesNotContain("anna@LAGO", second);
        Assert.Equal(first, second);
    }

    /// <summary>One cache for every caller of the test, as a host has one.</summary>
    private static IServiceProvider Cache()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching();
        return services.BuildServiceProvider();
    }

    private async Task<List<string>> Read(IServiceProvider cache, DocSession session, string user)
    {
        var services = new ServiceCollection()
            .AddSingleton(cache.GetRequiredService<ICacheStack>())
            .AddSingleton(cache.GetRequiredService<ICacheStackResolver>())
            .AddSingleton<ICurrentUser>(new TestUser(user))
            .AddSingleton<ITenantContext>(new TenantScope())
            .AddSingleton(_toggle)
            .AddScoped<IReadRepository<Review>>(_ => new Review.DocReadRepository(session))
            .BuildServiceProvider();
        var answer = await new CachedReviewsQuery.Invoker(services).RunAsync(new CachedReviewsQuery());
        return [.. answer.Value!.Select(r => r.Text).Order(StringComparer.Ordinal)];
    }
}
