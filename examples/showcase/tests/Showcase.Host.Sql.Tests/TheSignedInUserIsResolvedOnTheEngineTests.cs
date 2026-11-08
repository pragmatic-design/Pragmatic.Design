using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Showcase.Host.Sql.Tests;

/// <summary>
///     The Accounts module on the Sql engine has its user resolver (#595): it reads through the boundary's read repository, which is the
///     user's <c>IUserLookup</c>, so the per-user culture and a query bound with <c>[FromCurrentUser]</c> work as they do on EF Core. Each
///     user is registered through the host, and their culture is written in PostgreSQL by a connection of the test's own.
/// </summary>
[Collection(SqlHostCollection.Name)]
public sealed class TheSignedInUserIsResolvedOnTheEngineTests(SqlHostFixture fixture)
{
    /// <summary>
    ///     The typed <c>RoomUnavailableError</c> is shown in the user's <c>PreferredCulture</c>, with no <c>?culture=</c> and no
    ///     <c>Accept-Language</c>. Two users on one request path, two languages: a global setting would give them the same one.
    /// </summary>
    [Fact]
    public async Task A_users_preferred_culture_is_the_requests_culture()
    {
        var italian = await RoomUnavailableTitleFor(await Register("it"));
        var english = await RoomUnavailableTitleFor(await Register(null));

        italian.Should().Be("Camera Non Disponibile");
        english.Should().Be("Room Unavailable");
    }

    /// <summary>
    ///     <c>GetMyAccountQuery</c> reads the caller's own account: its <c>Id</c> is bound from the resolved user, not from the request. Two
    ///     users, each read back as themselves, so a query that read the first account would fail for one of them.
    /// </summary>
    [Fact]
    public async Task A_query_bound_from_the_current_user_reads_the_callers_own_account()
    {
        var first = await Register(null);
        var second = await Register("it");

        var mine = await Me(first);
        var theirs = await Me(second);

        mine.GetProperty("displayName").GetString().Should().Be(first.Email);
        theirs.GetProperty("displayName").GetString().Should().Be(second.Email);
        theirs.GetProperty("preferredCulture").GetString().Should().Be("it");
        mine.GetProperty("id").GetGuid().Should().NotBe(theirs.GetProperty("id").GetGuid());
    }

    private async Task<JsonElement> Me((string Key, string Email) user)
    {
        var response = await Caller(user.Key).GetAsync(new Uri("/api/accounts/me", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK, "Body: {0}", await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Registers a user through the host and, when given, stores <paramref name="preferredCulture" /> on their row.</summary>
    private async Task<(string Key, string Email)> Register(string? preferredCulture)
    {
        var email = $"culture.{Guid.NewGuid():N}@test.com";
        var registered = await Caller(null).PostAsJsonAsync("/identity/local/register", new { email, password = "Engine@Pass1" });
        registered.IsSuccessStatusCode.Should().BeTrue("Got {0}: {1}", registered.StatusCode, await registered.Content.ReadAsStringAsync());
        var key = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetString()
            ?? throw new InvalidOperationException("register returned no external identity key");

        if (preferredCulture is not null)
        {
            await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
            await connection.OpenAsync();
            await using var update = new NpgsqlCommand(
                "UPDATE \"AppUsers\" SET \"PreferredCulture\" = @culture WHERE \"Identity_ExternalIdentityKey\" = @key", connection);
            update.Parameters.AddWithValue("culture", preferredCulture);
            update.Parameters.AddWithValue("key", key);
            (await update.ExecuteNonQueryAsync()).Should().Be(1, "the registration stored the user's row");
        }

        return (key, email);
    }

    /// <summary>
    ///     Provokes the 409 <c>RoomUnavailableError</c> (five guests, a room for two) as <paramref name="user" />, and returns its title.
    /// </summary>
    private async Task<string?> RoomUnavailableTitleFor((string Key, string Email) user)
    {
        var guest = await Caller(null).PostAsJsonAsync("/api/guests", new { firstName = "Culture", lastName = "Guest", email = $"guest.{Guid.NewGuid():N}@test.com" });
        var guestId = (await guest.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var property = await Caller(null).PostAsJsonAsync("/api/properties", new { code = $"CU-{Guid.NewGuid():N}"[..12], name = "Culture Hotel", city = "Rome", country = "IT", starRating = 3 });
        var propertyId = (await property.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var roomType = await Caller(null).PostAsJsonAsync("/api/room-types", new { propertyId, name = "Culture Room", code = "CUR", baseRate = 100m, totalRooms = 5 });
        var roomTypeId = (await roomType.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await Caller(user.Key).PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new { guestId, propertyId, roomTypeId, checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"), checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"), numberOfGuests = 5 },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "Body: {0}", await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString();
    }

    /// <summary>A caller the host accepts, signed in as the user with <paramref name="externalIdentityKey" /> when one is given.</summary>
    private HttpClient Caller(string? externalIdentityKey)
    {
        var client = fixture.Host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "engine-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", externalIdentityKey ?? "engine-user");
        client.DefaultRequestHeaders.Add("X-User-Name", "Sql host test");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "*");
        if (externalIdentityKey is not null)
            client.DefaultRequestHeaders.Add("X-User-External-Key", externalIdentityKey);
        return client;
    }
}
