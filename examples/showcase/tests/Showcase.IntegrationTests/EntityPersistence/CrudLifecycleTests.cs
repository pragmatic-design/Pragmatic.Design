using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Booking;
using Showcase.Booking.Entities;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     Tests CRUD lifecycle via HTTP endpoints against real PostgreSQL.
///     Covers matrix features:
///       - Entity Declaration [Entity]
///       - Boundary Assignment [BelongsTo&lt;T&gt;]
///       - Auditable [Auditable] + IAuditable
///       - LogicKey [LogicKey]
///       - Default Value [DefaultValue]
///       - Multi-Tenancy ITenantEntity
/// </summary>
public class CrudLifecycleTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // Property — Create + Read + Audit + Tenant + LogicKey
    // =========================================================================

    [Fact]
    public async Task CreateProperty_ReturnsCreatedWithAuditFields()
    {
        var body = new
        {
            code = $"PROP-{Guid.NewGuid():N}".Substring(0, 15),
            name = "Grand Hotel Test",
            city = "Rome",
            country = "IT",
            starRating = 4
        };

        var response = await PostAsync("/api/properties", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        // Entity Declaration: has an Id (Guid)
        json.GetProperty("id").GetGuid().Should().NotBeEmpty();

        // Auditable: CreatedAt auto-populated
        json.GetProperty("createdAt").GetDateTimeOffset().Should().BeCloseTo(
            DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));

        // LogicKey: code persisted
        json.GetProperty("code").GetString().Should().Be(body.code);

        // Default values
        json.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task CreateProperty_ReadViaSearch_ReturnsSameEntity()
    {
        var code = $"SRCH-{Guid.NewGuid():N}".Substring(0, 15);
        var createBody = new
        {
            code,
            name = "Searchable Hotel",
            city = "Milan",
            country = "IT",
            starRating = 3
        };

        var createResponse = await PostAsync("/api/properties", createBody);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propertyId = created.GetProperty("id").GetGuid();

        // Read back via search
        var searchResponse = await GetRawAsync($"/api/properties/search?name=Searchable Hotel");
        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchResult = await searchResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var items = searchResult.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterOrEqualTo(1);

        // Verify the created entity is in the results
        var found = items.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == propertyId);
        found.Should().BeTrue();
    }

    // =========================================================================
    // Guest — Create + Update + Audit
    // =========================================================================

    [Fact]
    public async Task CreateGuest_ReturnsCreatedWithAllFields()
    {
        var body = new
        {
            firstName = "Mario",
            lastName = "Rossi",
            email = $"mario.{Guid.NewGuid():N}@test.com"
        };

        var response = await PostAsync("/api/guests", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("firstName").GetString().Should().Be("Mario");
        json.GetProperty("lastName").GetString().Should().Be("Rossi");
        json.GetProperty("email").GetString().Should().Be(body.email);

        // Default value: preferredLanguage = "en"
        json.GetProperty("preferredLanguage").GetString().Should().Be("en");

        // Auditable
        json.GetProperty("createdAt").GetDateTimeOffset().Should().BeCloseTo(
            DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task UpdateGuest_ChangesFields_PreservesCreatedAt()
    {
        // Create
        var createBody = new
        {
            firstName = "Luigi",
            lastName = "Verdi",
            email = $"luigi.{Guid.NewGuid():N}@test.com"
        };
        var createResponse = await PostAsync("/api/guests", createBody);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var guestId = created.GetProperty("id").GetGuid();
        var originalCreatedAt = created.GetProperty("createdAt").GetDateTimeOffset();

        // Update
        var updateBody = new
        {
            firstName = "Luigi Updated"
        };
        var updateResponse = await PutAsync($"/api/guests/{guestId}", updateBody);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var updated = await GetAsync<JsonElement>($"/api/guests/{guestId}");
        updated.GetProperty("firstName").GetString().Should().Be("Luigi Updated");

        // Auditable: CreatedAt preserved (BeCloseTo for PostgreSQL microsecond precision), UpdatedAt set
        updated.GetProperty("createdAt").GetDateTimeOffset().Should().BeCloseTo(
            originalCreatedAt, TimeSpan.FromMilliseconds(1));
        updated.GetProperty("updatedAt").GetDateTimeOffset().Should().BeCloseTo(
            DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    // =========================================================================
    // ICurrentUser: CreatedBy / UpdatedBy populated from headers
    // =========================================================================

    [Fact]
    public async Task CreateProperty_HeaderUser_SetsCreatedBy()
    {
        var body = new
        {
            code = $"CU-{Guid.NewGuid():N}"[..12],
            name = "CurrentUser Test",
            city = "Rome",
            country = "IT",
            starRating = 3
        };

        var response = await PostAsync("/api/properties", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propertyId = json.GetProperty("id").GetGuid();

        // Read from the database, not from the response. CreatedBy is a user identifier the server
        // owns, and [ReturnsDto<PropertyDetailDto>] keeps it off the wire. The audit interceptor fills
        // it from ICurrentUser, which reads the X-User-Id header — so it is asserted where it lives.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));
        // IgnoreQueryFilters: this scope is not a request, so it has no tenant and no caller. The
        // tenant and ownership filters are fail-closed, so without this the row is simply not there —
        // and the test would fail describing the wrong thing.
        var stored = await db.Set<Property>().AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.PersistenceId == propertyId);

        stored.Should().NotBeNull();
        stored!.CreatedBy.Should().Be("test-user",
            "CreatedBy should be populated from X-User-Id header via ICurrentUser");
    }

    [Fact]
    public async Task UpdateGuest_HeaderUser_SetsUpdatedBy()
    {
        var createBody = new
        {
            firstName = "CU",
            lastName = "Test",
            email = $"cu.{Guid.NewGuid():N}@test.com"
        };
        var createResponse = await PostAsync("/api/guests", createBody);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var guestId = created.GetProperty("id").GetGuid();

        var updateBody = new { firstName = "CU Updated" };
        var updateResponse = await PutAsync($"/api/guests/{guestId}", updateBody);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Read from the database: an update that declares no answer has no body, and
        // UpdatedBy is a user identifier the server owns, which no DTO carries.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(BookingBoundary));
        var stored = await db.Set<Guest>().AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.PersistenceId == guestId);

        stored.Should().NotBeNull();
        stored!.UpdatedBy.Should().Be("test-user",
            "UpdatedBy should be populated from X-User-Id header via ICurrentUser");
    }

    // =========================================================================
    // Amenity — Create + Default values
    // =========================================================================

    [Fact]
    public async Task CreateAmenity_WithCategory_ReturnsCreated()
    {
        var body = new
        {
            name = $"Pool {Guid.NewGuid():N}".Substring(0, 20),
            category = 1, // Pool
            iconName = "pool-icon"
        };

        var response = await PostAsync("/api/amenities", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("name").GetString().Should().Be(body.name);
        json.GetProperty("id").GetGuid().Should().NotBeEmpty();
    }

    // =========================================================================
    // RoomType — Create with FK to Property
    // =========================================================================

    [Fact]
    public async Task CreateRoomType_WithPropertyFk_ReturnsCreated()
    {
        // First create a property
        var propertyBody = new
        {
            code = $"RT-{Guid.NewGuid():N}".Substring(0, 12),
            name = "FK Test Hotel",
            city = "Florence",
            country = "IT",
            starRating = 3
        };
        var propertyResponse = await PostAsync("/api/properties", propertyBody);
        var property = await propertyResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propertyId = property.GetProperty("id").GetGuid();

        // Create room type linked to property
        var roomTypeBody = new
        {
            propertyId,
            name = "Deluxe Suite",
            code = "DLX",
            maxOccupancy = 3,
            baseRate = 250.00m,
            currency = "EUR",
            totalRooms = 10
        };
        var response = await PostAsync("/api/room-types", roomTypeBody);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("propertyId").GetGuid().Should().Be(propertyId);
        json.GetProperty("name").GetString().Should().Be("Deluxe Suite");
        json.GetProperty("baseRate").GetDecimal().Should().Be(250.00m);
    }
}
