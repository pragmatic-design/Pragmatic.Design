using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Documents.Markup;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;
using Showcase.Billing;
using Showcase.Billing.Endpoints;
using Showcase.Billing.Entities;
using Showcase.Booking.Dtos;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     <c>GET /api/invoices/{id}/pdf</c>: the invoice from <c>templates/invoice.pdxdoc</c>, in the guest's
///     language.
/// </summary>
public class TheInvoiceAsAPdfTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AnInvoice_DownloadsAsAPdf()
    {
        var invoiceId = await AnInvoiceAsync();

        var response = await Client.GetAsync($"/api/invoices/{invoiceId}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task AnInvoiceThatDoesNotExist_IsNotFound()
    {
        var response = await Client.GetAsync($"/api/invoices/{Guid.NewGuid()}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    ///     A guest who reads Italian gets an Italian invoice, with its number and its lines in it.
    /// </summary>
    /// <remarks>
    ///     Asserted on the document model that the endpoint renders — a PDF's text lives in a compressed
    ///     stream, so searching the bytes for a word finds nothing whether or not it is there. The ambient
    ///     culture of the test is not Italian, so the words can only be Italian because the guest is.
    /// </remarks>
    [Fact]
    public async Task TheDocument_IsInTheGuestsLanguage()
    {
        var invoiceId = await AnInvoiceAsync();
        var guest = new GuestDto { FirstName = "Ospite", LastName = "Italiano", PreferredLanguage = "it" };

        using var tenant = TenantScope.BeginScope("test-tenant");
        using var scope = Services.CreateScope();

        var invoice = await scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(BillingBoundary))
            .Set<Invoice>()
            .Include(i => i.LineItems)
            .SingleAsync(i => i.PersistenceId == invoiceId);

        var document = await DownloadInvoicePdfEndpoint.ComposeAsync(
            scope.ServiceProvider.GetRequiredService<IPdxTemplates>(), invoice, guest);

        var words = JsonSerializer.Serialize(document.Model);
        words.Should().Contain("Intestata a").And.Contain("Totale").And.Contain("Ospite Italiano");
        words.Should().Contain(invoice.InvoiceNumber);
        words.Should().NotContain("Billed to");
        document.Warnings.Should().BeEmpty("the endpoint provides every value the template names");
    }

    /// <summary>The invoice a confirmed reservation raises, found through the search route.</summary>
    private async Task<Guid> AnInvoiceAsync()
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
