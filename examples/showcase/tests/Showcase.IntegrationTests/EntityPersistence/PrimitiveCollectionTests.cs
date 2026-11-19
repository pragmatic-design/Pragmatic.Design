using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Repository;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     E2E for the source generator's "primitive collection" support on real PostgreSQL:
///     <c>Amenity.Keywords</c> (<c>List&lt;string&gt;</c>) persisted by EF Core as a JSON column,
///     surfaced in <c>AmenityDto</c> through the EF projection, and a collection-of-scalars query
///     filter (<c>SearchAmenitiesQuery.Names</c>) applied with SQL <c>IN</c>.
///     Exercises every path the feature touches end-to-end: EntityConfiguration (JSON column),
///     the create-mutation setter, the migration schema column, the [MapFrom] projection, the
///     repeated-query-key endpoint binding, and the In filter.
/// </summary>
public class PrimitiveCollectionTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<string> CreateAmenityAsync(params string[] keywords) => (await CreateAmenityWithIdAsync(keywords)).Name;

    private async Task<(string Name, Guid Id)> CreateAmenityWithIdAsync(params string[] keywords)
    {
        var name = $"PC-{Guid.NewGuid():N}"[..20];
        var resp = await PostAsync("/api/amenities", new { name, iconName = "wifi", keywords });
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        return (name, (await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid());
    }

    private async Task<JsonElement[]> SearchAsync(string queryString) =>
        [.. (await GetAsync<JsonElement>($"/api/amenities/search?{queryString}"))
            .GetProperty("items").EnumerateArray()];

    [Fact]
    public async Task PrimitiveCollection_Keywords_RoundTripsThroughJsonColumnAndProjection()
    {
        var keywords = new[] { "wifi", "wireless", "internet" };
        var name = await CreateAmenityAsync(keywords);

        var items = await SearchAsync($"names={name}");
        items.Should().HaveCount(1);

        var returned = items[0].GetProperty("keywords").EnumerateArray()
            .Select(k => k.GetString())
            .ToArray();
        returned.Should().BeEquivalentTo(keywords,
            "the List<string> primitive collection must persist to the JSON column and project back into the DTO");
    }

    [Fact]
    public async Task CollectionFilter_In_ReturnsOnlyAmenitiesWhoseNameIsInTheSet()
    {
        var a = await CreateAmenityAsync("x");
        var b = await CreateAmenityAsync("y");
        var c = await CreateAmenityAsync("z");

        // ?names=a&names=c → repeated query keys bound to a collection filter → SQL IN (a, c)
        var names = (await SearchAsync($"names={a}&names={c}&pageSize=200"))
            .Select(i => i.GetProperty("name").GetString())
            .ToArray();

        names.Should().Contain(a);
        names.Should().Contain(c);
        names.Should().NotContain(b, "b's name is not in the In set");
    }

    [Fact]
    public async Task CollectionFilter_WhenNamesAbsent_DoesNotFilterEverythingOut()
    {
        // The In filter must be skipped when the collection is null/empty — otherwise it would
        // emit an empty IN (...) and exclude every row. Scope the assertion with the Name Contains
        // filter so a large shared DB / paging can't hide the row.
        var name = await CreateAmenityAsync("solo");

        var found = (await SearchAsync($"name={name}"))
            .Select(i => i.GetProperty("name").GetString())
            .ToArray();

        found.Should().Contain(name);
    }

    [Fact]
    public async Task PrimitiveCollection_InPlaceMutation_IsTrackedAndPersisted()
    {
        // Create with two keywords via the working HTTP path; Name is the unique LogicKey.
        var (_, id) = await CreateAmenityWithIdAsync("alpha", "beta");

        // In-place mutation of the loaded collection (entity.Keywords.Add) — NOT a reassignment, and no Update call.
        // The store must see it on its own: EF Core through a value comparer, the engines through the snapshot they
        // take on load. Without either, the change is silently dropped at SaveChanges. Read by id: a specification
        // written here would not be compiled by an engine's generator, which runs on the module.
        using (var scope = Services.CreateScope())
        {
            var amenity = await scope.ServiceProvider.GetRequiredService<IRepository<Amenity>>().GetByIdAsync(id);
            amenity.Should().NotBeNull();
            amenity!.Keywords.Add("gamma");
            await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(CatalogBoundary)).SaveChangesAsync();
        }

        // Reload in a fresh scope — the in-place addition must have persisted.
        using (var scope = Services.CreateScope())
        {
            var reloaded = await scope.ServiceProvider.GetRequiredService<IReadRepository<Amenity>>().GetByIdAsync(id);
            reloaded!.Keywords.Should().BeEquivalentTo(["alpha", "beta", "gamma"],
                "an in-place primitive-collection mutation must be tracked and saved");
        }
    }
}
