using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests RemoteBoundary cross-service invocation.
///     NOTE: These tests verify the distributed topology compiles and routes correctly.
///     Full cross-service tests require 2 running hosts (Billing.Host + Host.Distributed)
///     which needs additional infrastructure (Docker Compose or dual WebApplicationFactory).
///     For now, we verify the monolith host handles the same endpoints (proving the SG
///     generates correct code for both topologies).
/// </summary>
public class RemoteBoundaryTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task RemoteBoundary_BillingEndpoints_AccessibleFromMonolith()
    {
        // In monolith mode, Billing endpoints are local (not remote).
        // In distributed mode, they'd be proxied via HttpInvoker to Billing.Host.
        // This test verifies the endpoints exist and respond in monolith mode.
        var response = await GetRawAsync("/api/invoices/search");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Billing endpoints should be accessible in monolith mode (same code path as remote)");
    }

    [Fact]
    public async Task RemoteBoundary_CrossBoundaryEvent_ReservationConfirmedCreatesInvoice()
    {
        // The cross-boundary flow: Booking.ReservationConfirmed → Billing.CreateDraftInvoice
        // In monolith: direct event handling. In distributed: would be via messaging.
        // This test verifies the monolith path works.
        var guest = await PostAsync<System.Text.Json.JsonElement>("/api/guests", new
        {
            firstName = "Remote",
            lastName = "Test",
            email = $"remote.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await PostAsync<System.Text.Json.JsonElement>("/api/properties", new
        {
            code = $"RB-{Guid.NewGuid():N}"[..12],
            name = $"RemoteProp-{Guid.NewGuid():N}"[..20],
            city = "Turin",
            country = "IT",
            starRating = 3
        });
        var propertyId = prop.GetProperty("id").GetGuid();

        var rt = await PostAsync<System.Text.Json.JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "Remote Room",
            code = $"RB{Guid.NewGuid():N}"[..3],
            baseRate = 250m,
            totalRooms = 3
        });
        var roomTypeId = rt.GetProperty("id").GetGuid();

        var resResponse = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(120).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(123).ToString("O"),
                numberOfGuests = 1
            }
        });
        resResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = await resResponse.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        // Confirm → triggers cross-boundary event → invoice created in Billing
        await PostAsync($"/api/reservations/{reservationId}/confirm", new { });

        // Verify invoice was created (cross-boundary event worked)
        var invoiceSearch = await GetAsync<System.Text.Json.JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = invoiceSearch.GetProperty("items");

        items.GetArrayLength().Should().BeGreaterOrEqualTo(1,
            "Cross-boundary event (ReservationConfirmed → CreateDraftInvoice) should create an invoice");
    }
}
