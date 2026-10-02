using System.Net;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;
using Pragmatic.Specification;

namespace Showcase.IntegrationTests.Queries;

/// <summary>
///     E2E coverage for Pragmatic.Specification: proves that a <b>composed</b> specification
///     (operator <c>&amp;</c> and the <c>AndIf</c> chain) is translated to SQL by the real
///     Npgsql provider — not just evaluated in-memory — and that the same specification produces
///     the same result through both paths (IQueryable→SQL and IsSatisfiedBy→compiled delegate).
///
///     Fills the gap flagged in the module review: specifications were exercised indirectly through
///     endpoints, but nothing asserted the ParameterReplacer-unified expression tree survives the
///     round-trip to Postgres.
/// </summary>
public class SpecificationCompositionTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ComposedSpec_OperatorAnd_TranslatesToSqlAndFilters()
    {
        // Unique city so this test is isolated from any other seeded property.
        var city = $"SpecCity-{Guid.NewGuid():N}"[..16];

        await CreatePropertyAsync("Premium", city, starRating: 5);
        await CreatePropertyAsync("Budget", city, starRating: 2);

        // PropertySpecifications.IsPremium() == IsActive() & MinStars(4)
        //   → AndSpecification whose ToExpression() unifies the two lambdas' parameters
        //   → EF Core must translate `IsActive && !IsDeleted && StarRating >= 4` to a single WHERE.
        var premium = PropertySpecifications.IsPremium();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        // IgnoreQueryFilters removes the ambient-tenant/soft-delete filters (there is no HTTP
        // request scope here), so we scope by tenant explicitly and let the SPEC do the rest.
        var matches = await db.Set<Property>()
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == "test-tenant" && p.City == city)
            .Where(premium)                       // ← ISpecification<T> overload → spec.ToExpression()
            .ToListAsync();

        matches.Should().ContainSingle("only the 5-star active property satisfies IsActive & MinStars(4)");
        matches[0].StarRating.Should().Be(5);
        matches[0].Name.Should().StartWith("Premium");

        // Dual-path honesty: the compiled delegate must agree with what SQL returned.
        premium.IsSatisfiedBy(matches[0]).Should().BeTrue();
    }

    [Fact]
    public async Task ComposedSpec_AndIfChain_TranslatesToSqlAndFilters()
    {
        var city = $"SpecCity-{Guid.NewGuid():N}"[..16];

        await CreatePropertyAsync("FiveStar", city, starRating: 5);
        await CreatePropertyAsync("FourStar", city, starRating: 4);
        await CreatePropertyAsync("ThreeStar", city, starRating: 3);

        // PropertySpecifications.Search builds IsActive().AndIf(city).AndIf(minStars) — the conditional
        // composition path. Both conditions are true here, so the resulting spec is a chain of
        // AndSpecification nodes that must round-trip to SQL as one predicate.
        var spec = PropertySpecifications.Search(city: city, minStars: 4);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var matches = await db.Set<Property>()
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == "test-tenant")
            .Where(spec)
            .ToListAsync();

        matches.Should().HaveCount(2, "only the 4- and 5-star properties in that city match");
        matches.Select(p => p.StarRating).Should().OnlyContain(s => s >= 4);
        matches.Should().OnlyContain(p => p.City == city);

        // The Count(spec) IQueryable extension must translate too and agree with the materialised list.
        var count = db.Set<Property>()
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == "test-tenant")
            .Count(spec);
        count.Should().Be(matches.Count);
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
