using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
/// E2E tests for the auto-generated [HasTags] trait endpoints, read side included:
/// POST /tags → GET /tags returns the tags with their values.
/// </summary>
/// <remarks>
///     These exercise the two tables a tag needs — the shared tag entity and the junction — against
///     a real database, which is the only place where a disagreement between the EF configuration
///     and the migrations schema shows up (the generated code compiles either way).
/// </remarks>
public class TagEndpointTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string ReservationsUrl = "/api/reservations";

    private static string TagsUrl(Guid reservationId) =>
        $"/api/booking/reservations/{reservationId}/tags";

    [Fact]
    public async Task ListTags_AfterAdds_ReturnsTagValues()
    {
        var reservationId = await CreateReservation();
        await AddTag(reservationId, "Urgent");
        await AddTag(reservationId, "VIP");

        var response = await GetRawAsync($"{TagsUrl(reservationId)}?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("totalCount").GetInt32().Should().Be(2);

        var items = json.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);

        // Value is normalized (case-insensitive by default), DisplayValue keeps the original casing.
        items.Select(i => i.GetProperty("value").GetString())
            .Should().BeEquivalentTo("urgent", "vip");
        items.Select(i => i.GetProperty("displayValue").GetString())
            .Should().BeEquivalentTo("Urgent", "VIP");

        foreach (var item in items)
        {
            item.GetProperty("reservationId").GetGuid().Should().Be(reservationId);
            item.GetProperty("tagId").GetGuid().Should().NotBeEmpty();
            item.GetProperty("scope").GetString().Should().Be("Reservation");
            item.GetProperty("addedBy").GetString().Should().Be("test-user");
        }
    }

    [Fact]
    public async Task ListTags_OtherReservationTags_AreNotReturned()
    {
        var mine = await CreateReservation();
        var other = await CreateReservation();
        await AddTag(mine, "MineOnly");
        await AddTag(other, "TheirsOnly");

        var response = await GetRawAsync(TagsUrl(mine));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("value").GetString())
            .Should().BeEquivalentTo("mineonly");
    }

    /// <summary>
    ///     The read endpoint is gated on its own permission. The default test client holds
    ///     <c>booking.*</c>, so the denial has to be proven with a client that does not.
    /// </summary>
    [Fact]
    public async Task ListTags_WithoutReadPermission_Returns403()
    {
        var client = CreateClientWithPermissions("booking.reservation.tags.add");

        var response = await client.GetAsync(TagsUrl(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<Guid> CreateReservation()
    {
        var guestBody = new { firstName = "TT", lastName = "Guest", email = $"tt.{Guid.NewGuid():N}@test.com" };
        var guest = await PostAsync<JsonElement>("/api/guests", guestBody);
        var guestId = guest.GetProperty("id").GetGuid();

        var propBody = new { code = $"TT-{Guid.NewGuid():N}"[..12], name = "TagProp", city = "Rome", country = "IT", starRating = 3 };
        var prop = await PostAsync("/api/properties", propBody);
        prop.EnsureSuccessStatusCode();
        var propJson = await prop.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propertyId = propJson.GetProperty("id").GetGuid();

        var rtBody = new { propertyId, name = "TT Room", code = "TTR", baseRate = 100m, totalRooms = 5 };
        var roomType = await PostAsync<JsonElement>("/api/room-types", rtBody);
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        var reservationBody = new
        {
            request = new
            {
                guestId, propertyId, roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 2
            }
        };
        var response = await PostAsync($"{ReservationsUrl}?api-version=1.0", reservationBody);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    private async Task<Guid> AddTag(Guid reservationId, string tagValue)
    {
        // The generated endpoint binds [FromBody] string: the body is a bare JSON string, not an object.
        var response = await PostAsync(TagsUrl(reservationId), tagValue);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, "add-tag response was: {0}", body);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}

/// <summary>
///     Tag behaviours end to end: idempotent re-tagging, de-duplication across entities (a guarantee
///     only because the unique index is in the database), and the generated DELETE endpoint.
/// </summary>
public class TagLifecycleTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private static string TagsUrl(Guid reservationId) =>
        $"/api/booking/reservations/{reservationId}/tags";

    private static string TagUrl(Guid reservationId, Guid tagId) =>
        $"/api/booking/reservations/{reservationId}/tags/{tagId}";

    [Fact]
    public async Task AddTag_Twice_IsIdempotent_AndDoesNotDuplicateTheLink()
    {
        var reservationId = await CreateReservation();

        var first = await AddTag(reservationId, "Urgent");
        var second = await AddTag(reservationId, "Urgent");

        second.Should().Be(first, "re-applying the same tag must return the existing tag id");

        var list = await GetAsync<JsonElement>($"{TagsUrl(reservationId)}?page=1&pageSize=10");
        list.GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SameTagOnTwoReservations_ReusesTheSameTagRow()
    {
        var first = await CreateReservation();
        var second = await CreateReservation();

        var tagOnFirst = await AddTag(first, "Seaview");
        var tagOnSecond = await AddTag(second, "seaview");

        tagOnSecond.Should().Be(tagOnFirst,
            "the tag is shared per boundary and matched case-insensitively — the unique (Value, Scope) " +
            "index is what keeps a second row from being created");
    }

    [Fact]
    public async Task RemoveTag_DropsTheLink_AndLeavesTheOtherTags()
    {
        var reservationId = await CreateReservation();
        var urgent = await AddTag(reservationId, "Urgent");
        await AddTag(reservationId, "VIP");

        var response = await DeleteAsync(TagUrl(reservationId, urgent));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await GetAsync<JsonElement>($"{TagsUrl(reservationId)}?page=1&pageSize=10");
        list.GetProperty("totalCount").GetInt32().Should().Be(1);
        list.GetProperty("items")[0].GetProperty("value").GetString().Should().Be("vip");
    }

    [Fact]
    public async Task RemoveTag_ThenAddItAgain_Works()
    {
        var reservationId = await CreateReservation();
        var tagId = await AddTag(reservationId, "Recurring");

        await DeleteAsync(TagUrl(reservationId, tagId));
        var readded = await AddTag(reservationId, "Recurring");

        // Losing its last use deletes the tag row, so re-adding mints a fresh one: without that,
        // every typo would leave a permanent row in a catalogue no endpoint can curate.
        readded.Should().NotBe(tagId);

        var list = await GetAsync<JsonElement>($"{TagsUrl(reservationId)}?page=1&pageSize=10");
        list.GetProperty("totalCount").GetInt32().Should().Be(1);
        list.GetProperty("items")[0].GetProperty("value").GetString().Should().Be("recurring");
    }

    [Fact]
    public async Task RemoveTag_KeepsTheTagWhenAnotherEntityStillUsesIt()
    {
        var first = await CreateReservation();
        var second = await CreateReservation();
        var tagId = await AddTag(first, "Shared");
        await AddTag(second, "Shared");

        await DeleteAsync(TagUrl(first, tagId));

        // Still linked elsewhere: the row must survive, and the surviving link must keep working.
        var stillThere = await GetAsync<JsonElement>($"{TagsUrl(second)}?page=1&pageSize=10");
        stillThere.GetProperty("totalCount").GetInt32().Should().Be(1);
        stillThere.GetProperty("items")[0].GetProperty("tagId").GetGuid().Should().Be(tagId);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<Guid> CreateReservation()
    {
        var guestBody = new { firstName = "TL", lastName = "Guest", email = $"tl.{Guid.NewGuid():N}@test.com" };
        var guest = await PostAsync<JsonElement>("/api/guests", guestBody);
        var guestId = guest.GetProperty("id").GetGuid();

        var propBody = new { code = $"TL-{Guid.NewGuid():N}"[..12], name = "TagProp", city = "Rome", country = "IT", starRating = 3 };
        var property = await PostAsync<JsonElement>("/api/properties", propBody);
        var propertyId = property.GetProperty("id").GetGuid();

        var rtBody = new { propertyId, name = "TL Room", code = "TLR", baseRate = 100m, totalRooms = 5 };
        var roomType = await PostAsync<JsonElement>("/api/room-types", rtBody);
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        var reservationBody = new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 2
            }
        };

        var response = await PostAsync("/api/reservations?api-version=1.0", reservationBody);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    private async Task<Guid> AddTag(Guid reservationId, string tagValue)
    {
        var response = await PostAsync(TagsUrl(reservationId), tagValue);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, "add-tag response was: {0}", body);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
