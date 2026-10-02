using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A moment is stored in UTC and read in the zone the caller asked for.
/// </summary>
/// <remarks>
///     <para>
///         The declaration is <c>[ToClientTimezone]</c> on the property and the caller's zone arrives on
///         the <c>X-Timezone</c> header. Nothing in the reading code converts anything: the generator
///         registers the property's behaviour and the serializer applies it.
///     </para>
///     <para>
///         ⚠️ Two references decide whether the attribute does anything, and neither is on the property.
///         The generator emits the behaviour only where <c>Pragmatic.Temporal.Json</c> is referenced —
///         the module's compilation, not the host's — and the conversion runs only where
///         <c>Pragmatic.Temporal.AspNetCore</c> is wired.
///     </para>
///     <para>
///         ⚠️ This is also the end-to-end proof that a modifier registered on the shared JSON seam
///         survives the host's wiring. A host that assigned its own resolver over the one contributors
///         wrapped would discard it silently, because a modifier that does not run leaves a well-formed
///         payload with the wrong values.
///     </para>
/// </remarks>
public class TheZoneAMomentIsReadInTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>The caller asks for Rome, and the instant comes back with Rome's offset.</summary>
    [Fact]
    public async Task AnInstant_IsReadInTheZoneTheCallerAsksFor()
    {
        var reservationId = await AReservationAsync();

        using var client = CreateClientAs("tz-caller", "Timezone Caller");
        client.DefaultRequestHeaders.Add("X-Timezone", "Europe/Rome");

        var row = await RowAsync(client, reservationId);
        var checkIn = DateTimeOffset.Parse(row.GetProperty("checkIn").GetString()!);

        checkIn.Offset.Should().NotBe(TimeSpan.Zero,
            "the caller asked for Europe/Rome, which is never at UTC+0");
    }

    /// <summary>
    ///     The control: the property beside it, which declares nothing, is untouched.
    /// </summary>
    /// <remarks>
    ///     Without it, "the instant is converted" would be satisfied by a serializer that shifted every
    ///     moment in the payload — a different feature, and a worse one.
    /// </remarks>
    [Fact]
    public async Task ThePropertyThatDeclaresNothing_IsLeftInUtc()
    {
        var reservationId = await AReservationAsync();

        using var client = CreateClientAs("tz-caller", "Timezone Caller");
        client.DefaultRequestHeaders.Add("X-Timezone", "Europe/Rome");

        var row = await RowAsync(client, reservationId);
        var checkOut = DateTimeOffset.Parse(row.GetProperty("checkOut").GetString()!);

        checkOut.Offset.Should().Be(TimeSpan.Zero, "CheckOut carries no attribute");
    }

    /// <summary>The second control: with no zone asked for, nothing moves.</summary>
    [Fact]
    public async Task WithNoZoneAsked_TheInstantStaysInUtc()
    {
        var reservationId = await AReservationAsync();

        var row = await RowAsync(Client, reservationId);
        var checkIn = DateTimeOffset.Parse(row.GetProperty("checkIn").GetString()!);

        checkIn.Offset.Should().Be(TimeSpan.Zero, "an unasked-for zone is no zone, and the store is UTC");
    }

    private static async Task<JsonElement> RowAsync(HttpClient client, Guid reservationId)
    {
        var response = await client.GetAsync("api/reservations/search?pageSize=100");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == reservationId);
    }

    private async Task<Guid> AReservationAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Zone",
            lastName = "Reader",
            email = $"zone.{Guid.NewGuid():N}@test.com"
        });

        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"TZ-{Guid.NewGuid():N}"[..12],
            name = $"TzProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId = property.GetProperty("id").GetGuid(),
            name = "TZ Room",
            code = $"TZ{Guid.NewGuid():N}"[..3],
            baseRate = 100m,
            totalRooms = 5
        });

        var created = await Client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId = guest.GetProperty("id").GetGuid(),
                propertyId = property.GetProperty("id").GetGuid(),
                roomTypeId = roomType.GetProperty("id").GetGuid(),
                checkIn = DateTimeOffset.UtcNow.AddDays(40).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(43).ToString("O"),
                numberOfGuests = 1
            }
        }, JsonOptions);

        created.EnsureSuccessStatusCode();

        return await created.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
