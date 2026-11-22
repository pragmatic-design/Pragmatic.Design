using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching;
using Pragmatic.Caching.Extensions;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A <c>[Cacheable]</c> query on the Sql engine reads through its cache (#882), and an owned entity's answer is the caller's own: two
///     callers with different rows never share an entry, as EF Core's executor partitions it.
/// </summary>
/// <remarks>
///     One cache for both callers, as a host has one: what keeps them apart is the key, not two caches. Each caller reads through a session
///     filtered for them, so the owner filter decides the rows, and a shared entry would hand the second caller the first caller's reviews.
/// </remarks>
[Collection("pragmatic databases")]
public class ACachedQueryIsTheCallersOwnTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Two_owners_reading_the_same_cached_query_each_get_their_own_rows(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var cache = Cache();
        using var anna = await databases.SessionAsync(dialect, Filters("anna"));
        await PragmaticRows.SeedAsync(anna, _toggle);
        // The same database read as bruno: a session on anna's connection, with his filters. Not disposed: the connection is anna's session's.
        var bruno = new SqlSession(anna.Connection, anna.Dialect) { Filters = Filters("bruno") };

        var hers = await Read(cache, anna, "anna");
        var his = await Read(cache, bruno, "bruno");

        Assert.Equal(["anna@MARE", "anna@RIVA"], hers); // anna@ALPE is unpublished: [VisibleWhen<PublishedReviews>] hides it
        Assert.Equal(["bruno@RIVA"], his);
    }

    /// <summary>The control: the same caller twice is served the entry, a row added in between is not seen until it expires.</summary>
    /// <remarks>Without it the case above holds for a query that reads the database every time, cached or not.</remarks>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_same_caller_is_served_the_cached_answer(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var cache = Cache();
        using var anna = await databases.SessionAsync(dialect, Filters("anna"));
        await PragmaticRows.SeedAsync(anna, _toggle);

        var first = await Read(cache, anna, "anna");
        var review = new Review { Text = "anna@LAGO", Published = true, Stars = 1 };
        review.SetOwnerId("anna");
        review.SetPropertyId((await anna.Query<Property>().ToListAsync()).First().Id);
        await anna.InsertAsync(review);
        var second = await Read(cache, anna, "anna");

        Assert.DoesNotContain("anna@LAGO", second);
        Assert.Equal(first, second);
    }

    private PragmaticSqlFilters Filters(string user) => new(new TestUser(user), new TenantScope(), new TestScopes("desk-north"), _toggle);

    /// <summary>One cache for every caller of the test, as a host has one.</summary>
    private static IServiceProvider Cache()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching();
        return services.BuildServiceProvider();
    }

    private async Task<List<string>> Read(IServiceProvider cache, SqlSession session, string user)
    {
        var services = new ServiceCollection()
            .AddSingleton(cache.GetRequiredService<ICacheStack>())
            .AddSingleton(cache.GetRequiredService<ICacheStackResolver>())
            .AddSingleton<ICurrentUser>(new TestUser(user))
            .AddSingleton<ITenantContext>(new TenantScope())
            .AddSingleton(_toggle)
            .AddScoped<IReadRepository<Review>>(_ => new Review.SqlReadRepository(session))
            .BuildServiceProvider();
        var answer = await new CachedReviewsQuery.Invoker(services).RunAsync(new CachedReviewsQuery());
        return [.. answer.Value!.Select(r => r.Text).Order(StringComparer.Ordinal)];
    }
}
