using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Queries;

/// <summary>
///     Tests [WithoutFilter&lt;T&gt;] declarative filter override via HTTP endpoints.
///     Verifies that the SearchDeletedAmenitiesQuery bypasses SoftDeleteFilter.
/// </summary>
public class WithoutFilterTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SearchDeletedAmenities_ReturnsDeletedItems()
    {
        // Arrange: client with both read and delete permissions
        var client = CreateClientWithPermissions(
            "catalog.amenity.read",
            "catalog.amenity.create",
            "catalog.amenity.delete");

        var uniqueName = $"WF-{Guid.NewGuid():N}"[..16];

        // Create amenity
        var createBody = new { name = uniqueName, category = 0, iconName = "test-icon" };
        var createResponse = await client.PostAsJsonAsync("/api/amenities", createBody);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var amenity = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = amenity.GetProperty("id").GetGuid();

        // Delete (soft-delete)
        var deleteResponse = await client.DeleteAsync($"/api/amenities/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, "a delete answers 204 and no body");

        // Act 1: normal search should NOT find the deleted amenity
        var normalSearch = await client.GetFromJsonAsync<JsonElement>(
            $"/api/amenities/search?name={uniqueName}&pageSize=50");
        ContainsId(normalSearch, id).Should().BeFalse(
            "Normal search should exclude soft-deleted amenities");

        // Act 2: admin search (WithoutFilter) SHOULD find the deleted amenity
        var adminResponse = await client.GetAsync(
            $"/api/amenities/deleted?name={uniqueName}&pageSize=50");
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await adminResponse.Content.ReadAsStringAsync());
        var adminSearch = await adminResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        ContainsId(adminSearch, id).Should().BeTrue(
            "[WithoutFilter<SoftDeleteFilter>] should bypass the soft-delete filter");
    }

    [Fact]
    public async Task SearchDeletedAmenities_ExcludesNonDeletedItems_WhenOnlyDeletedExist()
    {
        // Arrange: create 2 amenities, delete only one
        var client = CreateClientWithPermissions(
            "catalog.amenity.read",
            "catalog.amenity.create",
            "catalog.amenity.delete");

        var prefix = $"WF2-{Guid.NewGuid():N}"[..14];
        var nameToDelete = $"{prefix}-DEL";
        var nameToKeep = $"{prefix}-KEEP";

        // Create both
        var delResponse = await client.PostAsJsonAsync("/api/amenities",
            new { name = nameToDelete, category = 0, iconName = "del" });
        var deletedId = (await delResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("id").GetGuid();

        var keepResponse = await client.PostAsJsonAsync("/api/amenities",
            new { name = nameToKeep, category = 0, iconName = "keep" });
        var keptId = (await keepResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("id").GetGuid();

        // Delete one
        await client.DeleteAsync($"/api/amenities/{deletedId}");

        // Act: admin search sees both (filter is disabled)
        var adminSearch = await client.GetFromJsonAsync<JsonElement>(
            $"/api/amenities/deleted?name={prefix}&pageSize=50");

        ContainsId(adminSearch, deletedId).Should().BeTrue("Deleted amenity should be visible");
        ContainsId(adminSearch, keptId).Should().BeTrue("Non-deleted amenity should also be visible");
    }

    private static bool ContainsId(JsonElement pagedResult, Guid id)
    {
        return pagedResult.GetProperty("items").EnumerateArray()
            .Any(item => item.GetProperty("id").GetGuid() == id);
    }
}
