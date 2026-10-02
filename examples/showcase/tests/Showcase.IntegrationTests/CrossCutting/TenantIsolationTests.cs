using System.Net;
using System.Net.Http.Json;
using System.Linq;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests multi-tenancy isolation via ITenantEntity and TenantFilter.
///     Covers matrix features:
///       - TenantId auto-set from X-Tenant-Id header
///       - TenantFilter ensures cross-tenant data isolation
///       - Each tenant sees only their own data in search endpoints
/// </summary>
public class TenantIsolationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // Tenant A creates data → Tenant B cannot see it
    // =========================================================================

    [Fact]
    public async Task CreateProperty_TenantA_NotVisibleToTenantB()
    {
        using var tenantAClient = CreateClientAs("user-a", "User A", "tenant-iso-a");
        using var tenantBClient = CreateClientAs("user-b", "User B", "tenant-iso-b");

        // Tenant A creates a property
        var propertyCode = $"TA-{Guid.NewGuid():N}"[..12];
        var createResponse = await tenantAClient.PostAsJsonAsync("/api/properties", new
        {
            code = propertyCode,
            name = "Tenant A Hotel",
            city = "Rome",
            country = "IT",
            starRating = 4
        }, JsonOptions);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var property = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propertyId = property.GetProperty("id").GetGuid();
        propertyId.Should().NotBeEmpty("Created property should have a valid ID");
        property.GetProperty("name").GetString().Should().Be("Tenant A Hotel",
            "Created property should echo back the name");

        // Tenant B searches — should NOT see Tenant A's property
        var searchResponse = await tenantBClient.GetAsync("/api/properties/search");
        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchResult = await searchResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        ContainsId(searchResult, propertyId).Should().BeFalse(
            "TenantFilter should prevent Tenant B from seeing Tenant A's property");
    }

    // =========================================================================
    // Tenant B creates data → Tenant A cannot see it (reverse)
    // =========================================================================

    [Fact]
    public async Task CreateProperty_TenantB_NotVisibleToTenantA()
    {
        using var tenantAClient = CreateClientAs("user-a", "User A", "tenant-iso-c");
        using var tenantBClient = CreateClientAs("user-b", "User B", "tenant-iso-d");

        // Tenant B creates a property
        var createResponse = await tenantBClient.PostAsJsonAsync("/api/properties", new
        {
            code = $"TB-{Guid.NewGuid():N}"[..12],
            name = "Tenant B Hotel",
            city = "Milan",
            country = "IT",
            starRating = 5
        }, JsonOptions);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var property = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propertyId = property.GetProperty("id").GetGuid();

        // Tenant A searches — should NOT see Tenant B's property
        var searchResponse = await tenantAClient.GetAsync("/api/properties/search");
        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchResult = await searchResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        ContainsId(searchResult, propertyId).Should().BeFalse(
            "TenantFilter should prevent Tenant A from seeing Tenant B's property");
    }

    // =========================================================================
    // Both tenants create data → each only sees their own
    // =========================================================================

    [Fact]
    public async Task CrossTenantQuery_ReturnsOnlyOwnData()
    {
        // Guest does NOT implement ITenantEntity — tenant isolation doesn't apply to Guest.
        // Use Property instead, which implements ITenantEntity and has TenantFilter.
        using var tenantAClient = CreateClientAs("user-a", "User A", "tenant-iso-e");
        using var tenantBClient = CreateClientAs("user-b", "User B", "tenant-iso-f");

        // Tenant A creates a property
        var propAResponse = await tenantAClient.PostAsJsonAsync("/api/properties", new
        {
            code = $"XA-{Guid.NewGuid():N}"[..12],
            name = "TenantA Property",
            city = "Rome",
            country = "IT",
            starRating = 3
        }, JsonOptions);
        propAResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var propA = await propAResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propAId = propA.GetProperty("id").GetGuid();

        // Tenant B creates a property
        var propBResponse = await tenantBClient.PostAsJsonAsync("/api/properties", new
        {
            code = $"XB-{Guid.NewGuid():N}"[..12],
            name = "TenantB Property",
            city = "Milan",
            country = "IT",
            starRating = 4
        }, JsonOptions);
        propBResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var propB = await propBResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propBId = propB.GetProperty("id").GetGuid();

        // Verify isolation via GET by ID — each tenant can only see their own
        var getAOwn = await tenantAClient.GetAsync($"/api/properties/{propAId}");
        getAOwn.StatusCode.Should().Be(HttpStatusCode.OK,
            "Tenant A should be able to GET their own property");

        var getAOther = await tenantAClient.GetAsync($"/api/properties/{propBId}");
        getAOther.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Tenant A should NOT be able to GET Tenant B's property");

        var getBOwn = await tenantBClient.GetAsync($"/api/properties/{propBId}");
        getBOwn.StatusCode.Should().Be(HttpStatusCode.OK,
            "Tenant B should be able to GET their own property");

        var getBOther = await tenantBClient.GetAsync($"/api/properties/{propAId}");
        getBOther.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Tenant B should NOT be able to GET Tenant A's property");

        // Search isolation WITH caching: a cached page never crosses tenants.
        // /api/properties/search is [Cacheable]; the generated GetCacheKey() carries no tenant, but
        // the query executor prefixes the tenant discriminator into the effective cache key
        // (EfCoreQueryExecutor.BuildCacheKey). Both tenants issue the SAME search
        // (identical raw key). Tenant B searches first — populating the cache under its tenant-scoped
        // key — then Tenant A searches: A must get its own rows, never B's cached page.
        var searchB = await tenantBClient.GetAsync("/api/properties/search?name=Property");
        searchB.StatusCode.Should().Be(HttpStatusCode.OK);
        var namesB = (await searchB.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString())
            .ToList();
        namesB.Should().Contain("TenantB Property");
        namesB.Should().NotContain("TenantA Property",
            "Tenant B must never see Tenant A's rows, even from a warm cache");

        var searchA = await tenantAClient.GetAsync("/api/properties/search?name=Property");
        searchA.StatusCode.Should().Be(HttpStatusCode.OK);
        var namesA = (await searchA.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString())
            .ToList();
        namesA.Should().Contain("TenantA Property");
        namesA.Should().NotContain("TenantB Property",
            "Tenant B's cached search page must not leak to Tenant A");
    }

    // =========================================================================
    // Tenant isolation: GET by ID across tenants returns 404
    // =========================================================================

    [Fact]
    public async Task GetById_CrossTenant_ReturnsNotFound()
    {
        // Use Property (ITenantEntity with GET by ID endpoint) instead of Amenity
        // (which lacks a GET-by-ID endpoint and returns 405).
        using var tenantAClient = CreateClientAs("user-a", "User A", "tenant-iso-g");
        using var tenantBClient = CreateClientAs("user-b", "User B", "tenant-iso-h");

        // Tenant A creates a property
        var propResponse = await tenantAClient.PostAsJsonAsync("/api/properties", new
        {
            code = $"XG-{Guid.NewGuid():N}"[..12],
            name = "IsoProperty",
            city = "Rome",
            country = "IT",
            starRating = 3
        }, JsonOptions);
        propResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var prop = await propResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propId = prop.GetProperty("id").GetGuid();

        // Tenant A can access it
        var getA = await tenantAClient.GetAsync($"/api/properties/{propId}");
        getA.StatusCode.Should().Be(HttpStatusCode.OK,
            "Tenant A should be able to access their own property");
        var propBody = await getA.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        propBody.GetProperty("id").GetGuid().Should().Be(propId);
        propBody.GetProperty("name").GetString().Should().Be("IsoProperty",
            "Property fields should be returned correctly for the owning tenant");

        // Tenant B cannot access it — TenantFilter hides it
        var getB = await tenantBClient.GetAsync($"/api/properties/{propId}");
        getB.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "TenantFilter should return 404 when Tenant B tries to access Tenant A's property");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static bool ContainsId(JsonElement pagedResult, Guid id)
    {
        return pagedResult.GetProperty("items").EnumerateArray()
            .Any(item => item.GetProperty("id").GetGuid() == id);
    }
}
