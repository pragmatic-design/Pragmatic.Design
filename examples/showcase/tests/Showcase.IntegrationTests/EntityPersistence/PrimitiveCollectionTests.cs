using System.Net;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private async Task<string> CreateAmenityAsync(params string[] keywords)
    {
        var name = $"PC-{Guid.NewGuid():N}"[..20];
        var resp = await PostAsync("/api/amenities", new { name, iconName = "wifi", keywords });
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        return name;
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
        var name = await CreateAmenityAsync("alpha", "beta");

        // In-place mutation of the tracked collection (entity.Keywords.Add) — NOT a reassignment.
        // EF must detect this via a value comparer; a missing/incorrect comparer would silently
        // drop the change at SaveChanges (the V2 concern from the EF Core course review).
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var amenity = await db.Amenities.FirstAsync(a => a.Name == name);
            amenity.Keywords.Add("gamma");
            await db.SaveChangesAsync();
        }

        // Reload in a fresh context — the in-place addition must have persisted.
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var reloaded = await db.Amenities.AsNoTracking().FirstAsync(a => a.Name == name);
            reloaded.Keywords.Should().BeEquivalentTo(["alpha", "beta", "gamma"],
                "EF must track in-place primitive-collection mutations via a value comparer");
        }
    }
}
