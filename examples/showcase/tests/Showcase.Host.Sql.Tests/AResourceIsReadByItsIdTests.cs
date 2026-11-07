using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Showcase.Host.Sql.Tests;

/// <summary>
///     The read a <c>[Resource]</c> scaffolds answers the row of the id it was asked for, on the Sql engine (#572). Its query declared <c>Id</c>
///     with no <c>[Filter]</c>, which the engine reads as an inert input: the statement was unfiltered and answered the table's first row.
/// </summary>
[Collection(SqlHostCollection.Name)]
public sealed class AResourceIsReadByItsIdTests(SqlHostFixture fixture)
{
    [Fact]
    public async Task AGuestReadById_IsThatGuest_WhenOthersExist()
    {
        await CreateGuest("First");
        var second = await CreateGuest("Second");

        var read = await Caller().GetAsync(new Uri($"/api/booking/guests/{second}", UriKind.Relative));

        read.StatusCode.Should().Be(HttpStatusCode.OK, "Body: {0}", await read.Content.ReadAsStringAsync());
        (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("firstName").GetString().Should().Be("Second");
    }

    [Fact]
    public async Task ARoomTypeSoftDeleted_IsNoLongerRead()
    {
        var property = await Caller().PostAsJsonAsync("/api/properties", new { code = $"SD-{Guid.NewGuid():N}"[..12], name = "Soft Delete Hotel", city = "Rome", country = "IT", starRating = 3 });
        var propertyId = (await property.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        // Another room type, alive: an unfiltered read would answer it in place of the deleted one.
        (await Caller().PostAsJsonAsync("/api/room-types", new { propertyId, name = "Standing Suite", code = $"S{Guid.NewGuid():N}"[..6], maxOccupancy = 2, baseRate = 90.00m, currency = "EUR", totalRooms = 4 }))
            .IsSuccessStatusCode.Should().BeTrue();
        var roomType = await Caller().PostAsJsonAsync("/api/room-types", new { propertyId, name = "Restorable Suite", code = $"R{Guid.NewGuid():N}"[..6], maxOccupancy = 2, baseRate = 120.00m, currency = "EUR", totalRooms = 4 });
        roomType.IsSuccessStatusCode.Should().BeTrue("Got {0}: {1}", roomType.StatusCode, await roomType.Content.ReadAsStringAsync());
        var id = (await roomType.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Caller().GetAsync(new Uri($"/api/catalog/room-types/{id}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK, "the control: read before the delete");

        var deleted = await Caller().DeleteAsync(new Uri($"/api/catalog/room-types/{id}", UriKind.Relative));
        deleted.IsSuccessStatusCode.Should().BeTrue("Got {0}: {1}", deleted.StatusCode, await deleted.Content.ReadAsStringAsync());

        (await Caller().GetAsync(new Uri($"/api/catalog/room-types/{id}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a deleted row is not read, and no other row is read in its place");
    }

    private async Task<Guid> CreateGuest(string firstName)
    {
        var created = await Caller().PostAsJsonAsync("/api/booking/guests", new { firstName, lastName = "ById", email = $"byid.{Guid.NewGuid():N}@test.com", preferredLanguage = "en" });
        created.IsSuccessStatusCode.Should().BeTrue("Got {0}: {1}", created.StatusCode, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>A caller the host accepts: a tenant, an identity and every permission.</summary>
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
