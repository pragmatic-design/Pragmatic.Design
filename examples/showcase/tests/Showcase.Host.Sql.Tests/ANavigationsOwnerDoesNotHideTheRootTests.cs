using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Showcase.Host.Sql.Tests;

/// <summary>
///     A reservation reads its guest whoever owns the guest, as on EF Core (#580). Only the read's root is filtered by owner and scopes. The guest
///     and the property are created by one user, the reservation by another who cannot read the guest. The join to the guest carried the guest's
///     owner filter, so the required navigation's INNER JOIN dropped the reservation: 404 by id, and a search that counted one and listed none.
/// </summary>
[Collection(SqlHostCollection.Name)]
public sealed class ANavigationsOwnerDoesNotHideTheRootTests(SqlHostFixture fixture)
{
    [Fact]
    public async Task AReservation_IsReadWithItsGuest_ByACallerWhoCannotReadTheGuest()
    {
        var owner = "nav-owner-" + Guid.NewGuid().ToString("N")[..8];
        var reservationId = await CreateReservationAs(owner);

        var read = await Caller(owner, "booking.reservation.read,booking.reservation.create").GetAsync(new Uri($"/api/reservations/{reservationId}", UriKind.Relative));

        read.StatusCode.Should().Be(HttpStatusCode.OK, "Body: {0}", await read.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ASearch_ListsTheReservationItCounts_WithItsGuestsName()
    {
        var owner = "nav-owner-" + Guid.NewGuid().ToString("N")[..8];
        await CreateReservationAs(owner);

        var search = await Caller(owner, "booking.reservation.read,booking.reservation.create").GetAsync(new Uri("/api/reservations/search", UriKind.Relative));

        search.StatusCode.Should().Be(HttpStatusCode.OK, "Body: {0}", await search.Content.ReadAsStringAsync());
        var page = await search.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("totalCount").GetInt32().Should().Be(1, "the control: the caller owns one reservation");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(1, "the items are the rows the count counts");
        items[0].GetProperty("guestFirstName").GetString().Should().Be("Navigated");
    }

    /// <summary>The guest, property and room type as the host's own user; the reservation as <paramref name="owner" />.</summary>
    private async Task<Guid> CreateReservationAs(string owner)
    {
        var admin = Caller("engine-user", "*");
        var guest = await admin.PostAsJsonAsync("/api/booking/guests", new { firstName = "Navigated", lastName = "Guest", email = $"nav.{Guid.NewGuid():N}@test.com", preferredLanguage = "en" });
        guest.IsSuccessStatusCode.Should().BeTrue("Got {0}: {1}", guest.StatusCode, await guest.Content.ReadAsStringAsync());
        var guestId = (await guest.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var property = await admin.PostAsJsonAsync("/api/properties", new { code = $"NV-{Guid.NewGuid():N}"[..12], name = "Navigation Hotel", city = "Rome", country = "IT", starRating = 3 });
        property.IsSuccessStatusCode.Should().BeTrue("Got {0}: {1}", property.StatusCode, await property.Content.ReadAsStringAsync());
        var propertyId = (await property.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var roomType = await admin.PostAsJsonAsync("/api/room-types", new { propertyId, name = "Navigation Room", code = $"N{Guid.NewGuid():N}"[..6], maxOccupancy = 2, baseRate = 100.00m, currency = "EUR", totalRooms = 5 });
        roomType.IsSuccessStatusCode.Should().BeTrue("Got {0}: {1}", roomType.StatusCode, await roomType.Content.ReadAsStringAsync());
        var roomTypeId = (await roomType.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var reservation = await Caller(owner, "booking.reservation.read,booking.reservation.create").PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 1
            }
        });
        reservation.StatusCode.Should().Be(HttpStatusCode.Created, "Body: {0}", await reservation.Content.ReadAsStringAsync());
        return await reservation.Content.ReadFromJsonAsync<Guid>();
    }

    private HttpClient Caller(string user, string permissions)
    {
        var client = fixture.Host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "engine-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", user);
        client.DefaultRequestHeaders.Add("X-User-Name", "Navigation test");
        client.DefaultRequestHeaders.Add("X-User-Permissions", permissions);
        return client;
    }
}
