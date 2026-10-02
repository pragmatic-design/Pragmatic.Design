using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests that tenant isolation applies to mutations (PUT/DELETE), not just reads.
///     Complements TenantIsolationTests which covers GET/search isolation.
/// </summary>
public class TenantMutationIsolationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpdateProperty_CrossTenant_CannotModify()
    {
        using var tenantAClient = CreateClientAs("user-a", "User A", "tenant-mut-a");
        using var tenantBClient = CreateClientAs("user-b", "User B", "tenant-mut-b");

        // Tenant A creates a property
        var created = await CreatePropertyAsync(tenantAClient, "MutIso Hotel A");
        var propertyId = created.GetProperty("id").GetGuid();

        // Tenant B tries to update it — should fail (404 because TenantFilter hides it)
        var updateResponse = await tenantBClient.PutAsJsonAsync($"/api/properties/{propertyId}", new
        {
            code = $"HK-{Guid.NewGuid():N}"[..12],
            name = "Hijacked Hotel",
            city = "Evil City",
            country = "XX",
            starRating = 1
        }, JsonOptions);

        updateResponse.StatusCode.Should().BeOneOf(
            [HttpStatusCode.NotFound, HttpStatusCode.Forbidden],
            "Tenant B should not be able to update Tenant A's property — TenantFilter should block");

        // Verify the property is unchanged
        var verify = await tenantAClient.GetAsync($"/api/properties/{propertyId}");
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var property = await verify.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        property.GetProperty("name").GetString().Should().Be("MutIso Hotel A",
            "Property should be unchanged after cross-tenant update attempt");
    }

    [Fact]
    public async Task DeleteProperty_CrossTenant_CannotDelete()
    {
        using var tenantAClient = CreateClientAs("user-a", "User A", "tenant-mut-c");
        using var tenantBClient = CreateClientAs("user-b", "User B", "tenant-mut-d");

        // Tenant A creates a property
        var created = await CreatePropertyAsync(tenantAClient, "DeleteIso Hotel");
        var propertyId = created.GetProperty("id").GetGuid();

        // Tenant B tries to delete it — should fail
        var deleteResponse = await tenantBClient.DeleteAsync($"/api/properties/{propertyId}");

        deleteResponse.StatusCode.Should().BeOneOf(
            [HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.MethodNotAllowed],
            "Tenant B should not be able to delete Tenant A's property");

        // Verify the property still exists for Tenant A
        var verify = await tenantAClient.GetAsync($"/api/properties/{propertyId}");
        verify.StatusCode.Should().Be(HttpStatusCode.OK,
            "Property should still exist after cross-tenant delete attempt");
    }

    [Fact]
    public async Task UpdateGuest_CrossTenant_GuestNotTenantAware_StillAccessible()
    {
        // Guest does NOT implement ITenantEntity — verify this explicitly
        // by showing that different tenants CAN access the same guest (no isolation)
        using var tenantAClient = CreateClientAs("user-a", "User A", "tenant-mut-e");
        using var tenantBClient = CreateClientAs("user-b", "User B", "tenant-mut-f");

        var guest = await tenantAClient.PostAsJsonAsync("/api/guests", new
        {
            firstName = "Shared",
            lastName = "Guest",
            email = $"shared.{Guid.NewGuid():N}@test.com"
        }, JsonOptions);
        guest.StatusCode.Should().Be(HttpStatusCode.Created);
        var guestJson = await guest.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var guestId = guestJson.GetProperty("id").GetGuid();

        // Tenant B can access Guest (not tenant-scoped)
        var getFromB = await tenantBClient.GetAsync($"/api/guests/{guestId}");
        getFromB.StatusCode.Should().Be(HttpStatusCode.OK,
            "Guest is NOT ITenantEntity — should be accessible across tenants");
    }

    [Fact]
    public async Task TenantA_CanUpdateOwnProperty()
    {
        using var tenantAClient = CreateClientAs("user-a", "User A", "tenant-mut-g");

        var created = await CreatePropertyAsync(tenantAClient, "Own Hotel");
        var propertyId = created.GetProperty("id").GetGuid();

        var updateResponse = await tenantAClient.PutAsJsonAsync($"/api/properties/{propertyId}", new
        {
            code = $"OW-{Guid.NewGuid():N}"[..12],
            name = "Updated Own Hotel",
            city = "Rome",
            country = "IT",
            starRating = 5
        }, JsonOptions);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "Tenant A should be able to update their own property");

        var updated = await GetWithClientAsync<JsonElement>(tenantAClient, $"/api/properties/{propertyId}");
        updated.GetProperty("name").GetString().Should().Be("Updated Own Hotel");
    }

    [Fact]
    public async Task RestoreProperty_CrossTenant_CannotRestoreAnotherTenantsSoftDeletedRow()
    {
        using var tenantAClient = CreateClientAs("user-a", "User A", "tenant-mut-h");
        using var tenantBClient = CreateClientAs("user-b", "User B", "tenant-mut-i");

        // Tenant B creates a property and soft-deletes it.
        var created = await CreatePropertyAsync(tenantBClient, "RestoreIso Hotel");
        var propertyId = created.GetProperty("id").GetGuid();

        var delete = await tenantBClient.DeleteAsync($"/api/properties/{propertyId}");
        delete.StatusCode.Should().BeOneOf(
            [HttpStatusCode.OK, HttpStatusCode.NoContent],
            "Tenant B should be able to soft-delete its own property");

        // Tenant A tries to restore Tenant B's soft-deleted property by id — must fail. Restore
        // bypasses the "SoftDelete" filter to find the row but keeps the "Tenant" filter, so the row
        // is invisible to Tenant A. Regression guard for MT-H1: a blanket IgnoreQueryFilters() dropped
        // tenant isolation too, letting one tenant un-delete another tenant's row.
        var restoreByA = await tenantAClient.PostAsync($"/api/properties/{propertyId}/restore", null);
        restoreByA.StatusCode.Should().BeOneOf(
            [HttpStatusCode.NotFound, HttpStatusCode.Forbidden],
            "Tenant A must not be able to restore Tenant B's soft-deleted property");

        // Tenant B restores its own — proves the row still exists, so A's failure was isolation, not
        // a row that was already gone.
        var restoreByB = await tenantBClient.PostAsync($"/api/properties/{propertyId}/restore", null);
        restoreByB.StatusCode.Should().BeOneOf(
            [HttpStatusCode.OK, HttpStatusCode.NoContent],
            "Tenant B should be able to restore its own soft-deleted property");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async Task<JsonElement> CreatePropertyAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/properties", new
        {
            code = $"TM-{Guid.NewGuid():N}"[..12],
            name,
            city = "Rome",
            country = "IT",
            starRating = 4
        }, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }
}
