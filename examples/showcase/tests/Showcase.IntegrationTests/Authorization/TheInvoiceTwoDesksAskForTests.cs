using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     <c>[RequireAnyPermission]</c>: one route, two audiences, and neither holds the
///     other's permission.
/// </summary>
/// <remarks>
///     <para>
///         Billing reads an invoice with <c>billing.invoice.read</c>. At checkout the front desk reads
///         the same invoice, and holds <c>booking.reservation.read</c> and nothing of billing's. The
///         OR is what keeps that one route: the alternative is a second endpoint returning the same
///         DTO, and two routes over one read are how a permission change comes to be applied on one
///         of them.
///     </para>
///     <para>
///         ⚠️ The two halves are one test on purpose. "The front desk reads it" alone is satisfied by
///         an endpoint that lost its requirement altogether — which is exactly what
///         <c>[RequireAnyPermission]</c> would look like if the generator wrote no policy for it, and
///         a route that answers everybody is the failure this repository has already shipped once.
///         The refusal below is the other half.
///     </para>
/// </remarks>
public class TheInvoiceTwoDesksAskForTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EitherDesk_ReadsTheSameInvoice()
    {
        var invoiceId = await AnInvoiceAsync();

        using var billing = Desk("billing.invoice.read");
        using var frontDesk = Desk("booking.reservation.read");

        (await billing.GetAsync(Route(invoiceId))).StatusCode.Should().Be(HttpStatusCode.OK,
            "billing holds the first of the two permissions the route accepts");
        (await frontDesk.GetAsync(Route(invoiceId))).StatusCode.Should().Be(HttpStatusCode.OK,
            "the front desk holds the second, and holds nothing of billing's");
    }

    /// <summary>
    ///     The control: an OR of two permissions is not the absence of one.
    /// </summary>
    /// <remarks>
    ///     The caller is authenticated and holds a real permission of each boundary — just not either
    ///     of the two the route names. Without this, both assertions above are satisfied by an
    ///     endpoint that requires nothing at all.
    /// </remarks>
    [Fact]
    public async Task ACallerHoldingNeither_IsRefused()
    {
        var invoiceId = await AnInvoiceAsync();

        using var neither = Desk("billing.payment.read,booking.guest.read");

        (await neither.GetAsync(Route(invoiceId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static string Route(Guid invoiceId) => $"/api/invoices/{invoiceId}?api-version=1.0";

    /// <summary>
    ///     A caller holding exactly <paramref name="permissions" />, and the data scope the invoice
    ///     sits in.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The scope is not decoration. <c>Invoice</c> is <c>[HasAccessScopes]</c> and the invoice
    ///     bills in EUR, so <c>EurInvoiceScopeRule</c> puts it in the <c>billing-eu</c> bucket: a
    ///     caller outside it is answered <b>404</b> by the row filter, whatever permission they hold.
    ///     Without the scope this test measured visibility and called it authorization — it failed on
    ///     the billing desk with NotFound, which is the right answer to a different question.
    /// </remarks>
    private HttpClient Desk(string permissions)
    {
        var client = CreateClientAs($"desk-{Guid.NewGuid():N}"[..12], "Desk User");
        client.DefaultRequestHeaders.Remove("X-User-Permissions");
        client.DefaultRequestHeaders.Add("X-User-Permissions", permissions);
        client.DefaultRequestHeaders.Add("X-User-Scopes", "billing-eu");
        return client;
    }

    /// <summary>The invoice a confirmed reservation raises, found through the search route.</summary>
    private async Task<Guid> AnInvoiceAsync()
    {
        var reservationId = await AConfirmedReservationAsync();

        var response = await Client.GetAsync($"/api/invoices/search?reservationId={reservationId}");
        response.EnsureSuccessStatusCode();

        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("items");

        items.GetArrayLength().Should().Be(1, "confirming a reservation raises exactly one invoice");
        return items[0].GetProperty("id").GetGuid();
    }

    private async Task<Guid> AConfirmedReservationAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Two",
            lastName = "Desks",
            email = $"desks.{Guid.NewGuid():N}@test.com"
        });

        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"TD-{Guid.NewGuid():N}"[..12],
            name = $"DeskProp-{Guid.NewGuid():N}"[..20],
            city = "Turin",
            country = "IT",
            starRating = 3
        });

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId = property.GetProperty("id").GetGuid(),
            name = "TD Room",
            code = $"TD{Guid.NewGuid():N}"[..3],
            baseRate = 120m,
            totalRooms = 4
        });

        var created = await Client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId = guest.GetProperty("id").GetGuid(),
                propertyId = property.GetProperty("id").GetGuid(),
                roomTypeId = roomType.GetProperty("id").GetGuid(),
                checkIn = DateTimeOffset.UtcNow.AddDays(20).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(22).ToString("O"),
                numberOfGuests = 2
            }
        }, JsonOptions);

        created.EnsureSuccessStatusCode();

        // A bare Guid on the wire, not an envelope: this route answers with the id itself.
        var reservationId = await created.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        (await Client.PostAsJsonAsync($"/api/reservations/{reservationId}/confirm", new { }, JsonOptions))
            .EnsureSuccessStatusCode();

        return reservationId;
    }
}
