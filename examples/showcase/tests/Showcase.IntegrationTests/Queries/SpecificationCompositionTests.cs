using System.Net;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Queries;

/// <summary>
///     E2E coverage for Pragmatic.Specification: proves that a <b>composed</b> specification
///     (operator <c>&amp;</c> and the <c>AndIf</c> chain) is translated by the store — not just
///     evaluated in memory — and that the same specification produces the same result through both
///     paths (the repository's read and IsSatisfiedBy→compiled delegate).
/// </summary>
/// <remarks>
///     Read through <c>IReadRepository</c>, which each engine translates its own way: EF Core to SQL
///     through the expression tree, the Sql and documents engines through the predicates they compile
///     for the specifications the module declares, composed node by node. The composition here is of
///     declared specifications only: one written in the test would not be compiled by the engines.
/// </remarks>
public class SpecificationCompositionTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ComposedSpec_OperatorAnd_TranslatesAndFilters()
    {
        // Unique city so this test is isolated from any other seeded property.
        var city = $"SpecCity-{Guid.NewGuid():N}"[..16];

        await CreatePropertyAsync("Premium", city, starRating: 5);
        await CreatePropertyAsync("Budget", city, starRating: 2);

        // PropertySpecifications.IsPremium() == IsActive() & MinStars(4), here also & InCity(city):
        // an AndSpecification of AndSpecifications, which the store must read as one predicate.
        var premium = PropertySpecifications.IsPremium();

        var matches = await FindAsync(r => r.FindAsync(premium & PropertySpecifications.InCity(city)));

        matches.Should().ContainSingle("only the 5-star active property satisfies IsActive & MinStars(4)");
        matches[0].StarRating.Should().Be(5);
        matches[0].Name.Should().StartWith("Premium");

        // Dual-path honesty: the compiled delegate must agree with what the store returned.
        premium.IsSatisfiedBy(matches[0]).Should().BeTrue();
    }

    [Fact]
    public async Task ComposedSpec_AndIfChain_TranslatesAndFilters()
    {
        var city = $"SpecCity-{Guid.NewGuid():N}"[..16];

        await CreatePropertyAsync("FiveStar", city, starRating: 5);
        await CreatePropertyAsync("FourStar", city, starRating: 4);
        await CreatePropertyAsync("ThreeStar", city, starRating: 3);

        // PropertySpecifications.Search builds IsActive().AndIf(city).AndIf(minStars) — the conditional
        // composition path. Both conditions are true here, so the resulting spec is a chain of
        // AndSpecification nodes that must reach the store as one predicate.
        var spec = PropertySpecifications.Search(city: city, minStars: 4);

        var matches = await FindAsync(r => r.FindAsync(spec));

        matches.Should().HaveCount(2, "only the 4- and 5-star properties in that city match");
        matches.Select(p => p.StarRating).Should().OnlyContain(s => s >= 4);
        matches.Should().OnlyContain(p => p.City == city);

        // The count through the same specification must translate too and agree with the materialised list.
        var count = await FindAsync(r => r.CountAsync(spec));
        count.Should().Be(matches.Count);
    }

    /// <summary>
    ///     A read in the suite's tenant with the row filters lifted, so the specification alone decides: outside a request there is no
    ///     caller for them to filter by.
    /// </summary>
    private async Task<TResult> FindAsync<TResult>(Func<IReadRepository<Property>, Task<TResult>> read)
    {
        using var tenant = TenantScope.BeginScope("test-tenant");
        using var scope = Services.CreateScope();
        using var filters = scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>().DisableAll();
        return await read(scope.ServiceProvider.GetRequiredService<IReadRepository<Property>>());
    }

    private async Task CreatePropertyAsync(string namePrefix, string city, int starRating)
    {
        var body = new
        {
            code = $"SP-{Guid.NewGuid():N}"[..12],
            name = $"{namePrefix}-{Guid.NewGuid():N}"[..20],
            city,
            country = "IT",
            starRating
        };
        var response = await PostAsync("/api/properties", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
