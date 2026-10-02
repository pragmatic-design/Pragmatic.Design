using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests FilterMode toggles (P2):
///       - Normal mode: all filters active
///       - Search with soft-deleted records visible (admin scenario)
/// </summary>
public class FilterModeTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task NormalMode_SoftDeletedEntity_NotVisibleInSearch()
    {
        // Create and then delete an amenity
        var amenity = await PostAsync<JsonElement>("/api/amenities", new
        {
            name = $"FilterMode-{Guid.NewGuid():N}"[..25],
            icon = "pool"
        });
        var amenityId = amenity.GetProperty("id").GetGuid();

        // Delete it (soft-delete)
        var deleteResponse = await DeleteAsync($"/api/amenities/{amenityId}");
        deleteResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Created);

        // Normal search should NOT show deleted amenity
        var searchResponse = await GetAsync<JsonElement>("/api/amenities/search");
        var items = searchResponse.GetProperty("items");

        var found = false;
        for (var i = 0; i < items.GetArrayLength(); i++)
        {
            if (items[i].GetProperty("id").GetGuid() == amenityId)
            {
                found = true;
                break;
            }
        }

        found.Should().BeFalse(
            "SoftDelete filter in Normal mode should hide deleted entities from search results");
    }

    [Fact]
    public async Task NormalMode_TenantIsolation_OnlyShowsCurrentTenant()
    {
        // Create property as tenant-a
        using var tenantAClient = CreateClientAs("user-a", "User A", "tenant-alpha");
        var propA = await tenantAClient.PostAsJsonAsync("/api/properties", new
        {
            code = $"TA-{Guid.NewGuid():N}"[..12],
            name = $"TenantA-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        propA.EnsureSuccessStatusCode();

        // Search as tenant-b — should NOT see tenant-a's property
        using var tenantBClient = CreateClientAs("user-b", "User B", "tenant-beta");
        var searchResponse = await tenantBClient.GetAsync("/api/properties/search");
        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await searchResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var items = body.GetProperty("items");

        // tenant-beta should see 0 results (no properties created for this tenant)
        // This verifies TenantFilter isolation
        items.GetArrayLength().Should().Be(0,
            "TenantFilter should isolate data between tenants");
    }
}
