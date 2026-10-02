using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     E2E test for the Pragmatic.Documents XLSX integration.
///     Seeds an invoice (via reservation confirm → auto-created invoice), then exports all invoices
///     through <c>GET /api/invoices/export.xlsx</c> and asserts a valid, non-empty XLSX is returned.
/// </summary>
public class InvoiceExportTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [Fact]
    public async Task ExportInvoices_ReturnsXlsxFile()
    {
        // Arrange: create + confirm a reservation so an invoice exists to export.
        await CreateAndConfirmReservationAsync();

        // Act
        var response = await GetRawAsync("/api/invoices/export.xlsx");

        // Assert: 200 + XLSX content type
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(XlsxContentType);

        // Assert: body is a valid, non-empty XLSX (OOXML packages are ZIP archives → "PK" magic)
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
        bytes[0].Should().Be((byte)'P');
        bytes[1].Should().Be((byte)'K');
    }

    // =========================================================================
    // Seeding helpers (mirror DomainEventTests: guest + property + room-type →
    // reservation → confirm → auto-created invoice)
    // =========================================================================

    private async Task CreateAndConfirmReservationAsync()
    {
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync();
        var reservationId = await CreateReservationAsync(guestId, propertyId, roomTypeId);

        var confirmResponse = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> CreatePrerequisitesAsync()
    {
        var guestResponse = await Client.PostAsJsonAsync("/api/guests", new
        {
            firstName = "XE",
            lastName = "Guest",
            email = $"xe.{Guid.NewGuid():N}@test.com"
        }, JsonOptions);
        guestResponse.EnsureSuccessStatusCode();
        var guest = await guestResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var guestId = guest.GetProperty("id").GetGuid();

        var propertyResponse = await Client.PostAsJsonAsync("/api/properties", new
        {
            code = $"XE-{Guid.NewGuid():N}"[..12],
            name = $"XEProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        }, JsonOptions);
        propertyResponse.EnsureSuccessStatusCode();
        var property = await propertyResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propertyId = property.GetProperty("id").GetGuid();

        var roomTypeResponse = await Client.PostAsJsonAsync("/api/room-types", new
        {
            propertyId,
            name = "XE Room",
            code = "XER",
            baseRate = 100m,
            totalRooms = 5
        }, JsonOptions);
        roomTypeResponse.EnsureSuccessStatusCode();
        var roomType = await roomTypeResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        return (guestId, propertyId, roomTypeId);
    }

    private async Task<Guid> CreateReservationAsync(Guid guestId, Guid propertyId, Guid roomTypeId)
    {
        var response = await Client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
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
        }, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
