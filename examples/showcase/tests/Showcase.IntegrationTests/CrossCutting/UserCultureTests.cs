using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Accounts.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A user's stored <c>PreferredCulture</c> decides the language of the errors they are shown, with
///     no <c>?culture=</c> and no <c>Accept-Language</c> on the request.
/// </summary>
/// <remarks>
///     <para>
///         The path from the attribute to the wire has several links, and each one hides the next: the
///         resolver has to be generated; the opt-in has to register everything it needs, and the
///         resolver cannot ask for a bare <c>DbContext</c>, which no Pragmatic application registers;
///         the key a user is looked up by has to be composed one way, or it matches nobody; the i18n
///         middleware has to run after authentication, or the provider sees an anonymous request; and
///         the resolver that localizes errors has to be reachable from the endpoint path.
///     </para>
///     <para>
///         They stayed hidden because they all fail the same way. A resolver that finds no user, a user
///         with no preference, and a localizer nobody calls all produce the default language — which is
///         also what a correct system produces most of the time.
///     </para>
/// </remarks>
public class UserCultureTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>Registers a user, stores a culture against it, returns the key that addresses it.</summary>
    private async Task<string> CreateUserWithCultureAsync(string? preferredCulture)
    {
        var email = $"culture-{Guid.NewGuid():N}@test.com";
        var register = await PostAsync("/identity/local/register", new { email, password = "Culture@Pass1" });
        register.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);

        var key = (await register.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetString()
            ?? throw new InvalidOperationException("register returned no external identity key");

        if (preferredCulture is not null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var user = await db.Set<AppUser>().FirstAsync(u => u.Identity!.ExternalIdentityKey == key);
            user.PreferredCulture = preferredCulture;
            await db.SaveChangesAsync();
        }

        return key;
    }

    /// <summary>
    ///     Provokes the typed <c>RoomUnavailableError</c> (409) as the given user and returns the title
    ///     and the detail from the RFC 7807 body. Its code, <c>ROOM_UNAVAILABLE</c>, keys
    ///     <c>error.room.unavailable.title</c> and <c>error.room.unavailable.detail</c>, which the
    ///     Showcase translates.
    /// </summary>
    /// <remarks>
    ///     The detail is read as well as the title because the title cannot tell the two apart:
    ///     <c>RoomUnavailableError.Title</c> is the string "Room Unavailable", which is also its
    ///     English translation, so a default-culture assertion on the title passes whether or not
    ///     anything was resolved. ⚠️ The host's default culture is <c>en-US</c> while the file it
    ///     ships is <c>en.json</c>, and the configuration is deliberately left that way: the provider
    ///     falls back from a culture to its language, and this is the assertion that the framework
    ///     resolves the default culture, rather than an example renamed until it does.
    /// </remarks>
    private async Task<(string? Title, string? Detail)> RoomUnavailableProblemForAsync(string? externalIdentityKey)
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Culture",
            lastName = "Guest",
            email = $"culture.{Guid.NewGuid():N}@test.com"
        });
        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"CU-{Guid.NewGuid():N}"[..12],
            name = $"CUProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId = property.GetProperty("id").GetGuid(),
            name = "CU Room",
            code = "CUR",
            baseRate = 100m,
            totalRooms = 5
        });

        var client = externalIdentityKey is null ? Client : CreateClientForIdentity(externalIdentityKey);

        // MaxOccupancy defaults to 2; asking for 5 returns the typed error rather than a validation one.
        var response = await client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId = guest.GetProperty("id").GetGuid(),
                propertyId = property.GetProperty("id").GetGuid(),
                roomTypeId = roomType.GetProperty("id").GetGuid(),
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 5
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return (body.GetProperty("title").GetString(), body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AUserWhoPrefersItalian_IsShownAnItalianError()
    {
        var (title, detail) = await RoomUnavailableProblemForAsync(await CreateUserWithCultureAsync("it"));

        title.Should().Be("Camera Non Disponibile");
        detail.Should().Be("La camera richiesta non è disponibile per le date selezionate");
    }

    /// <summary>
    ///     The counter-test, and what makes the first mean something: without it, a host whose default
    ///     happened to be Italian would pass while resolving nothing.
    /// </summary>
    [Fact]
    public async Task AUserWithNoPreference_IsShownTheDefaultLanguage()
    {
        var (title, detail) =
            await RoomUnavailableProblemForAsync(await CreateUserWithCultureAsync(preferredCulture: null));

        title.Should().Be("Room Unavailable");
        detail.Should().Be("The requested room is not available for the selected dates");
    }

    /// <summary>
    ///     Two users, one request path, two languages — the property the feature exists for, and the one
    ///     a single-user assertion cannot tell apart from a global setting.
    /// </summary>
    [Fact]
    public async Task TwoUsersWithDifferentPreferences_AreShownTwoDifferentLanguages()
    {
        var italian = await RoomUnavailableProblemForAsync(await CreateUserWithCultureAsync("it"));
        var english = await RoomUnavailableProblemForAsync(await CreateUserWithCultureAsync("en"));

        italian.Title.Should().Be("Camera Non Disponibile");
        english.Title.Should().Be("Room Unavailable");
    }

    /// <summary>
    ///     A stored value that no longer parses defers to the default rather than failing the request —
    ///     the user must stay able to reach the page where they could correct it.
    /// </summary>
    [Fact]
    public async Task AUserWithAnUnparseableCulture_IsShownTheDefaultRatherThanAnError()
        => (await RoomUnavailableProblemForAsync(await CreateUserWithCultureAsync("not-a-culture-at-all")))
            .Title.Should().Be("Room Unavailable");

    /// <summary>
    ///     An anonymous caller has no preference to read, and must still get a well-formed error rather
    ///     than a failure from the resolver looking for a user that is not there.
    /// </summary>
    [Fact]
    public async Task AnUnidentifiedCaller_IsShownTheDefaultLanguage()
        => (await RoomUnavailableProblemForAsync(externalIdentityKey: null))
            .Title.Should().Be("Room Unavailable");
}
