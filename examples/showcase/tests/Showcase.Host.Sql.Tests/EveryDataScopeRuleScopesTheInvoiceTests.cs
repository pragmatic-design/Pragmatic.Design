using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Showcase.Host.Sql.Tests;

/// <summary>
///     The Showcase registers two materialized data scope rules on <c>Invoice</c>, EUR and USD. A EUR invoice raised by a confirmed reservation
///     is stored with <c>scope:billing-eu</c> on the Sql engine (#597). The engine's unit of work reads the rules as <c>IEnumerable&lt;DataScopeRule&gt;</c>,
///     and that shape resolved the last rule registered twice, so the EUR rule never ran and the invoice kept only its creator's scope.
/// </summary>
[Collection(SqlHostCollection.Name)]
public sealed class EveryDataScopeRuleScopesTheInvoiceTests(SqlHostFixture fixture)
{
    [Fact]
    public async Task AEuroInvoice_IsStoredInTheEuroScope_AndNotTheDollarOne()
    {
        var reservationId = await AConfirmedReservationAsync();

        var scopes = await InvoiceScopesAsync(reservationId);

        scopes.Should().Contain("scope:billing-eu", "the EUR rule matches the invoice");
        scopes.Should().NotContain("scope:billing-us", "the control: the USD rule ran too, and does not match");
    }

    /// <summary>
    ///     The invoice's scopes as the row holds them, read past the host. The confirmation's handler raises the invoice before the request
    ///     answers, as the EF Core host's suite reads it (<c>TheInvoiceTwoDesksAskForTests</c> searches it right after the confirm).
    /// </summary>
    private async Task<string> InvoiceScopesAsync(Guid reservationId)
    {
        await using var connection = new NpgsqlConnection(fixture.FinancialConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT \"AccessScopes\"::text FROM \"Invoices\" WHERE \"ReservationId\" = @id", connection);
        command.Parameters.AddWithValue("id", reservationId);
        return await command.ExecuteScalarAsync() as string ?? throw new InvalidOperationException("Confirming the reservation raised no invoice.");
    }

    private async Task<Guid> AConfirmedReservationAsync()
    {
        var caller = Caller();
        var guest = await caller.PostAsJsonAsync("/api/booking/guests", new { firstName = "Scoped", lastName = "Invoice", email = $"scoped.{Guid.NewGuid():N}@test.com", preferredLanguage = "en" });
        guest.IsSuccessStatusCode.Should().BeTrue("Got {0}: {1}", guest.StatusCode, await guest.Content.ReadAsStringAsync());
        var guestId = (await guest.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var property = await caller.PostAsJsonAsync("/api/properties", new { code = $"SI-{Guid.NewGuid():N}"[..12], name = "Scoped Hotel", city = "Turin", country = "IT", starRating = 3 });
        var propertyId = (await property.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var roomType = await caller.PostAsJsonAsync("/api/room-types", new { propertyId, name = "Scoped Room", code = $"S{Guid.NewGuid():N}"[..6], maxOccupancy = 2, baseRate = 120.00m, currency = "EUR", totalRooms = 4 });
        var roomTypeId = (await roomType.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var created = await caller.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(20).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(22).ToString("O"),
                numberOfGuests = 2
            }
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, "Body: {0}", await created.Content.ReadAsStringAsync());
        var reservationId = await created.Content.ReadFromJsonAsync<Guid>();
        var confirmed = await caller.PostAsJsonAsync($"/api/reservations/{reservationId}/confirm", new { });
        confirmed.IsSuccessStatusCode.Should().BeTrue("Got {0}: {1}", confirmed.StatusCode, await confirmed.Content.ReadAsStringAsync());
        return reservationId;
    }

    private HttpClient Caller()
    {
        var client = fixture.Host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "engine-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", "engine-user");
        client.DefaultRequestHeaders.Add("X-User-Name", "Sql host test");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "*");
        return client;
    }
}
