using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>The invoice the PDF tests render: raised by confirming a reservation, through the HTTP API alone.</summary>
public abstract class InvoiceTestsBase(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>The invoice a confirmed reservation raises, found through the search route.</summary>
    protected async Task<Guid> AnInvoiceAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Pdf",
            lastName = "Guest",
            email = $"pdf.{Guid.NewGuid():N}@test.com",
        });

        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"PDF-{Guid.NewGuid():N}"[..12],
            name = $"PdfProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3,
        });

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId = property.GetProperty("id").GetGuid(),
            name = "Pdf Room",
            code = "PDR",
            baseRate = 100m,
            totalRooms = 5,
        });

        var created = await Client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId = guest.GetProperty("id").GetGuid(),
                propertyId = property.GetProperty("id").GetGuid(),
                roomTypeId = roomType.GetProperty("id").GetGuid(),
                checkIn = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(10).ToString("O"),
                numberOfGuests = 2,
            },
        }, JsonOptions);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = await created.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        var confirmed = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });
        confirmed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var search = await Client.GetAsync($"/api/invoices/search?reservationId={reservationId}");
        search.EnsureSuccessStatusCode();
        var items = (await search.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("items");

        items.GetArrayLength().Should().Be(1, "confirming a reservation raises exactly one invoice");
        return items[0].GetProperty("id").GetGuid();
    }
}
