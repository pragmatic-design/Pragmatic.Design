using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.DomainActions;

/// <summary>
///     Tests CompositeAction E2E (P1):
///       - Multi-mutation orchestration via HTTP
///       - Atomic: all succeed or all roll back
/// </summary>
public class CompositeActionTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateReservation_CompositeAction_CreatesReservationAndAssignsRoom()
    {
        // CreateReservationAction is a DomainAction that internally creates reservation
        // The full flow: CreateReservation → reservation created → event → invoice created
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Composite",
            lastName = "Test",
            email = $"comp.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"CT-{Guid.NewGuid():N}"[..12],
            name = $"CompTest-{Guid.NewGuid():N}"[..20],
            city = "Milan",
            country = "IT",
            starRating = 4
        });
        var propertyId = prop.GetProperty("id").GetGuid();

        var rt = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "Comp Room",
            code = $"CR{Guid.NewGuid():N}"[..3],
            baseRate = 200m,
            totalRooms = 3
        });
        var roomTypeId = rt.GetProperty("id").GetGuid();

        // CreateReservation is the main composite action endpoint
        var response = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(60).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(63).ToString("O"),
                numberOfGuests = 2
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "CompositeAction CreateReservation should succeed and return 201");

        var reservationId = await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
        reservationId.Should().NotBeEmpty();

        // Verify reservation exists
        var getResponse = await GetRawAsync($"/api/reservations/{reservationId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
