using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Queries;

/// <summary>
///     A grouping declared as a query answers on its own route, and answers the same numbers the
///     rows do.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Executed, and that is the point. The declarative grouping — <c>[QueryView]</c> with
///         <c>[GroupBy]</c> and the aggregate attributes — has existed for a long time and generated a
///         <c>Build</c> nobody could reach over HTTP: a view has no route and no entry in the
///         published contract, so every aggregate in the consumer was a hand-written action that
///         wrote the grouping again in LINQ. Nothing in this repository ever ran one.
///     </para>
///     <para>
///         The control is not "it returned 200": it is that the counts and the sums equal what the
///         un-grouped route reports for the same reservations. A grouping that produced plausible
///         numbers from the wrong SQL would pass every test that only checked it ran.
///     </para>
/// </remarks>
public class AnAggregateReadIsAQueryTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>The aggregate route answers, grouped by status.</summary>
    [Fact]
    public async Task TheAggregateQuery_AnswersOnItsOwnRoute()
    {
        var propertyId = await APropertyWithReservationsAsync(2);

        var response = await GetRawAsync($"/api/reservations/by-status?propertyId={propertyId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an aggregate read is a read, with a route of its own");

        var rows = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        rows.ValueKind.Should().Be(JsonValueKind.Array, "a grouping that declares no paging is a list");
        rows.GetArrayLength().Should().BeGreaterThan(0);
    }

    /// <summary>
    ///     And the numbers are the ones the rows carry.
    /// </summary>
    /// <remarks>
    ///     The same reservations, counted twice: once by the database through the declared grouping,
    ///     once here from the un-grouped search. ⚠️ Without this the test would pass on any SQL that
    ///     returned a number.
    /// </remarks>
    [Fact]
    public async Task TheGroupedNumbers_AreTheOnesTheRowsCarry()
    {
        var propertyId = await APropertyWithReservationsAsync(3);

        var grouped = await GetAsync<JsonElement>($"/api/reservations/by-status?propertyId={propertyId}");
        var rows = await GetAsync<JsonElement>(
            $"/api/reservations/search?propertyId={propertyId}&pageSize=100");

        var reservations = rows.GetProperty("items").EnumerateArray().ToList();
        reservations.Should().NotBeEmpty("the arrangement created them");

        var expectedCount = reservations.Count;
        var expectedTotal = reservations.Sum(r => r.GetProperty("totalAmount").GetDecimal());

        var groups = grouped.EnumerateArray().ToList();
        groups.Sum(g => g.GetProperty("reservations").GetInt32()).Should().Be(expectedCount,
            "every reservation belongs to exactly one group");
        groups.Sum(g => g.GetProperty("totalAmount").GetDecimal()).Should().Be(expectedTotal,
            "the sum of the groups is the sum of the rows");
    }

    /// <summary>
    ///     The control: the filter the query declares narrows the grouping too.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without it, "the numbers are right" would be satisfied by a grouping that ignored the
    ///     query's <c>Apply</c> and counted the whole table — which is precisely what happens if the
    ///     aggregation is handed the raw set instead of the filtered one.
    /// </remarks>
    [Fact]
    public async Task TheQuerysFilter_NarrowsTheGrouping()
    {
        var mine = await APropertyWithReservationsAsync(2);
        var someoneElses = await APropertyWithReservationsAsync(3);

        var groups = await GetAsync<JsonElement>($"/api/reservations/by-status?propertyId={mine}");
        var others = await GetAsync<JsonElement>($"/api/reservations/by-status?propertyId={someoneElses}");

        groups.EnumerateArray().Sum(g => g.GetProperty("reservations").GetInt32()).Should().Be(2);
        others.EnumerateArray().Sum(g => g.GetProperty("reservations").GetInt32()).Should().Be(3);
    }

    /// <summary>A property with a room type and <paramref name="count"/> reservations on it.</summary>
    private async Task<Guid> APropertyWithReservationsAsync(int count)
    {
        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"AG-{Guid.NewGuid():N}"[..12],
            name = $"AggProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var propertyId = property.GetProperty("id").GetGuid();

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "Agg Room",
            code = "AGR",
            baseRate = 100m,
            totalRooms = 20
        });
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        for (var i = 0; i < count; i++)
        {
            var guest = await PostAsync<JsonElement>("/api/guests", new
            {
                firstName = "Agg",
                lastName = "Guest",
                email = $"agg.{Guid.NewGuid():N}@test.com"
            });

            var response = await PostAsync("/api/reservations?api-version=1.0", new
            {
                request = new
                {
                    guestId = guest.GetProperty("id").GetGuid(),
                    propertyId,
                    roomTypeId,
                    checkIn = DateTimeOffset.UtcNow.AddDays(7 + i).ToString("O"),
                    checkOut = DateTimeOffset.UtcNow.AddDays(10 + i).ToString("O"),
                    numberOfGuests = 2
                }
            });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        return propertyId;
    }
}
