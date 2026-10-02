using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests cross-cutting concerns via HTTP endpoints.
///     Covers matrix features:
///       - Result Pattern Result&lt;T&gt;, VoidResult&lt;T&gt;
///       - Domain Events RaiseEvent()
///       - Multi-Tenancy ITenantEntity
///       - Specification Specification&lt;T&gt;
/// </summary>
public class CrossCuttingTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // Result Pattern: successful operations return proper result
    // =========================================================================

    [Fact]
    public async Task CreateProperty_Success_ReturnsResultBody()
    {
        var body = new
        {
            code = $"RP-{Guid.NewGuid():N}"[..12],
            name = "Result Pattern Hotel",
            city = "Rome",
            country = "IT",
            starRating = 3
        };

        var response = await PostAsync("/api/properties", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        // Result<T> unwrapped: response is the entity directly
        json.GetProperty("id").GetGuid().Should().NotBeEmpty();
        json.GetProperty("code").GetString().Should().Be(body.code);
    }

    // =========================================================================
    // Result Pattern: error responses
    // =========================================================================

    [Fact]
    public async Task CreateReservation_InvalidState_ReturnsErrorResult()
    {
        var reservationId = await CreateReservationAsync();

        // Cancel
        await PostAsync($"/api/reservations/{reservationId}/cancel",
            new { reason = "Test" });

        // Try to confirm cancelled reservation — invalid transition
        var response = await PostAsync(
            $"/api/reservations/{reservationId}/confirm", new { });

        // Should return 409 Conflict (custom error type)
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // =========================================================================
    // Multi-Tenancy: TenantId auto-set
    // =========================================================================

    [Fact]
    public async Task CreateProperty_TenantHeader_SetsTenantId()
    {
        var body = new
        {
            code = $"MT-{Guid.NewGuid():N}"[..12],
            name = "Tenant Test Hotel",
            city = "Rome",
            country = "IT",
            starRating = 3
        };

        var response = await PostAsync("/api/properties", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        // TenantId is an infrastructure property: EntityJsonModifier strips it from API
        // responses by design, so its assignment is verified behaviorally — the entity is
        // reachable under the creating tenant and invisible to another tenant.
        json.TryGetProperty("tenantId", out _).Should().BeFalse(
            "EntityJsonModifier excludes TenantId from API responses");
        var propertyId = json.GetProperty("id").GetGuid();

        var sameTenant = await GetRawAsync($"/api/properties/{propertyId}");
        sameTenant.StatusCode.Should().Be(HttpStatusCode.OK,
            "the creating tenant must see its own property");

        using var otherTenant = CreateClientAs("other-user", tenantId: "other-tenant");
        var crossTenant = await otherTenant.GetAsync($"/api/properties/{propertyId}");
        crossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Property implements ITenantEntity — TenantId from X-Tenant-Id must scope visibility");
    }

    // =========================================================================
    // Domain Events: reservation confirm → invoice created
    // =========================================================================

    [Fact]
    public async Task DomainEvent_ReservationConfirmed_InvoiceCreated()
    {
        var reservationId = await CreateReservationAsync();

        // Confirm triggers ReservationConfirmed domain event
        var confirm = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Event handler should have created an invoice
        var search = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = search.GetProperty("items");

        items.GetArrayLength().Should().BeGreaterOrEqualTo(1,
            "ReservationConfirmed event should trigger invoice creation");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<Guid> CreateReservationAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "CC",
            lastName = "Guest",
            email = $"cc.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"CC-{Guid.NewGuid():N}"[..12],
            name = $"CCProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var propertyId = prop.GetProperty("id").GetGuid();

        var rt = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "CC Room",
            code = "CCR",
            baseRate = 100m,
            totalRooms = 5
        });
        var roomTypeId = rt.GetProperty("id").GetGuid();

        var response = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(10).ToString("O"),
                numberOfGuests = 2
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
