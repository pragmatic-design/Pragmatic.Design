using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     Tests soft delete and restore via HTTP endpoints.
///     Covers matrix features:
///       - Soft Delete [SoftDelete] + ISoftDelete
///       - Soft Delete Cascade [SoftDelete(Cascade=true)]
///       - Restore (Mutation)
/// </summary>
public class SoftDeleteTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DeleteProperty_SetsIsDeletedTrue()
    {
        var property = await CreatePropertyAsync();
        var id = property.GetProperty("id").GetGuid();

        var deleteResponse = await DeleteAsync($"/api/properties/{id}");

        // 204 and no body: the deleted row is not an answer, so the flags are read where they are
        // stored.
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));
        // IgnoreQueryFilters: the row is soft-deleted, which is exactly what the filters hide, and this
        // scope is not a request, so it has no tenant or caller either.
        var stored = await db.Set<Property>().AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(p => p.PersistenceId == id);
        stored.IsDeleted.Should().BeTrue();
        stored.DeletedAt.Should().NotBeNull();
        stored.DeletedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task DeletedProperty_NotVisibleInSearch()
    {
        var uniqueName = $"SoftDel-{Guid.NewGuid():N}"[..20];
        var property = await CreatePropertyAsync(uniqueName);
        var id = property.GetProperty("id").GetGuid();

        // Verify visible in search before delete (use unique name to bypass cache)
        var beforeSearch = await SearchPropertiesByNameAsync(uniqueName);
        ContainsId(beforeSearch, id).Should().BeTrue("Property should be visible before deletion");

        // Delete
        await DeleteAsync($"/api/properties/{id}");

        // Verify NOT visible in search (soft-delete global query filter)
        // Use different pageSize to ensure a different cache key
        var afterSearch = await GetAsync<JsonElement>(
            $"/api/properties/search?name={uniqueName}&pageSize=50");
        ContainsId(afterSearch, id).Should().BeFalse("Deleted property should be excluded by query filter");
    }

    [Fact]
    public async Task RestoreProperty_ResetsDeletedFields_VisibleAgain()
    {
        var uniqueName = $"Restore-{Guid.NewGuid():N}"[..20];
        var property = await CreatePropertyAsync(uniqueName);
        var id = property.GetProperty("id").GetGuid();

        // Delete then restore
        await DeleteAsync($"/api/properties/{id}");
        (await GetRawAsync($"/api/properties/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the control: while deleted, the read does not find the row");
        var restoreResponse = await PostAsync($"/api/properties/{id}/restore", new { });

        restoreResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var restored = await GetRawAsync($"/api/properties/{id}");
        restored.StatusCode.Should().Be(HttpStatusCode.OK, "a soft-deleted row is filtered out of the read");

        // Verify visible in search again (use unique name for deterministic results)
        var search = await SearchPropertiesByNameAsync(uniqueName);
        ContainsId(search, id).Should().BeTrue("Restored property should be visible again");
    }

    [Fact]
    public async Task DeleteAmenity_SoftDeletes_NotInSearch()
    {
        var name = $"AmenDel-{Guid.NewGuid():N}"[..20];
        var createBody = new { name, category = 0, iconName = "test" };
        var createResponse = await PostAsync("/api/amenities", createBody);
        var amenity = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = amenity.GetProperty("id").GetGuid();

        // Delete
        var deleteResponse = await DeleteAsync($"/api/amenities/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, "a delete answers 204 and no body");

        // Verify not in search (search returns all, check by ID)
        var search = await GetRawAsync("/api/amenities/search");
        var searchResult = await search.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        ContainsId(searchResult, id).Should().BeFalse("Deleted amenity should be excluded by query filter");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<JsonElement> CreatePropertyAsync(string? name = null)
    {
        var body = new
        {
            code = $"SD-{Guid.NewGuid():N}"[..12],
            name = name ?? $"SoftDel-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        };
        var response = await PostAsync("/api/properties", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private async Task<JsonElement> SearchAllPropertiesAsync()
    {
        var response = await GetRawAsync("/api/properties/search");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private async Task<JsonElement> SearchPropertiesByNameAsync(string name)
    {
        return await GetAsync<JsonElement>($"/api/properties/search?name={name}");
    }

    private static bool ContainsId(JsonElement pagedResult, Guid id)
    {
        return pagedResult.GetProperty("items").EnumerateArray()
            .Any(item => item.GetProperty("id").GetGuid() == id);
    }
}
