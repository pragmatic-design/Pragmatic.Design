using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A declared specification, used through what the generator writes for it — and returning the
///     same rows as the form it replaces.
/// </summary>
/// <remarks>
///     <para>
///         <c>PropertySpecifications</c> was written once and then used in its most awkward form:
///         <c>query.Where(PropertySpecifications.InCity(city).ToExpression())</c> names three things to say one.
///         The generator recognises a <c>Specification&lt;TEntity&gt;</c> by type — the same
///         recognition <c>[Query]</c> classes already relied on — and now emits the two surfaces it is
///         consumed through.
///     </para>
///     <para>
///         ⚠️ The generator test asserts the extensions are emitted and compile. An extension that
///         compiles and filters <em>nothing</em> would pass it: this case is what makes the claim
///         mean something, by comparing against the hand-written form on the same rows and by
///         requiring the row that must not match to be absent.
///     </para>
/// </remarks>
public class DeclaredSpecificationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
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

    /// <summary>And the four terminal reads answer for the same specification.</summary>
    [Fact]
    public async Task TheRepositoryTerminals_ReadThroughTheSameSpecification()
    {
        var city = $"City{Guid.NewGuid():N}"[..12];
        var wanted = await APropertyInAsync(city);

        using var scope = Services.CreateScope();

        // The repository applies the tenant filter, and outside a request there is no ambient tenant:
        // both reads would answer nothing and "the same rows" would be satisfied by two empty lists.
        // The tenant is set so the comparison has rows to be about.
        using var tenant = scope.ServiceProvider.GetRequiredService<IMutableTenantContext>()
            .SetTenant("test-tenant");

        var repository = scope.ServiceProvider.GetRequiredService<IReadRepository<Property>>();

        var found = await repository.FindInCityAsync(city);
        var directly = await repository.FindAsync(PropertySpecifications.InCity(city));

        found.Select(p => p.PersistenceId).Should().Equal(directly.Select(p => p.PersistenceId),
            "the terminal is the same call with the specification named instead of built");
        found.Select(p => p.PersistenceId).Should().Contain(wanted);

        (await repository.CountInCityAsync(city)).Should().Be(found.Count);
        (await repository.AnyInCityAsync(city)).Should().BeTrue();
        (await repository.FirstInCityOrDefaultAsync(city)).Should().NotBeNull();
    }

    /// <summary>The control: a city nobody is in answers nothing, through every form.</summary>
    /// <remarks>
    ///     Without it, an extension that ignored its argument would pass both cases above — the rows it
    ///     returned would still match the hand-written form's, because both would return everything.
    /// </remarks>
    [Fact]
    public async Task ACityNobodyIsIn_AnswersNothing()
    {
        var absent = $"Nowhere{Guid.NewGuid():N}"[..14];

        using var scope = Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<IMutableTenantContext>()
            .SetTenant("test-tenant");

        var repository = scope.ServiceProvider.GetRequiredService<IReadRepository<Property>>();

        (await repository.AnyInCityAsync(absent)).Should().BeFalse();
        (await repository.CountInCityAsync(absent)).Should().Be(0);
        (await repository.FirstInCityOrDefaultAsync(absent)).Should().BeNull();
    }
}
