using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     The queryable extension the generator writes for a declared specification, over EF Core's set.
/// </summary>
/// <remarks>
///     EF Core's own: the extension composes onto an <c>IQueryable</c>, and the engines read through their own query model, not one.
///     The repository terminals the generator writes for the same specification are <see cref="DeclaredSpecificationTests" />, which runs
///     on every engine (#873).
/// </remarks>
public class DeclaredSpecificationQueryableTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<Guid> APropertyInAsync(string city)
    {
        var created = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"SPC{Guid.NewGuid():N}"[..12],
            name = $"Spec {city}",
            city,
            country = "IT",
            starRating = 4
        });

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>The queryable extension and the hand-written form select the same rows.</summary>
    [Fact]
    public async Task TheQueryableExtension_SelectsWhatTheHandWrittenFormSelects()
    {
        var city = $"City{Guid.NewGuid():N}"[..12];
        var wanted = await APropertyInAsync(city);
        var other = await APropertyInAsync($"Other{Guid.NewGuid():N}"[..12]);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));

        // IgnoreQueryFilters on both sides: outside a request there is no ambient tenant, and the
        // subject here is the specification, not the filter pipeline.
        var handWritten = await db.Set<Property>()
            .IgnoreQueryFilters()
            .Where(PropertySpecifications.InCity(city).ToExpression())
            .Select(p => p.PersistenceId)
            .ToListAsync();

        var generated = await db.Set<Property>()
            .IgnoreQueryFilters()
            .InCity(city)
            .Select(p => p.PersistenceId)
            .ToListAsync();

        generated.Should().Equal(handWritten, "the extension delegates to the same specification");
        generated.Should().Contain(wanted);
        generated.Should().NotContain(other,
            "a filter that let everything through would satisfy the comparison above on its own");
    }
}
