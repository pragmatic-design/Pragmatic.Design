using Pragmatic.Testing.Assertions;
using Pragmatic.Testing;
using Pragmatic.Tests.Generated;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     W5 (testing two-tier): the generated typed client Api.{Boundary}.{Name}Async — routes
///     and verbs resolved at compile time from ApiRoutes + endpoint contracts — used exactly
///     as a developer would in hand-written tests.
/// </summary>
public class TypedClientTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TypedClient_CreateAndGetGuest_RoundTrips()
    {
        var email = $"typed.{Guid.NewGuid():N}@test.com";

        var created = await Api.Booking.CreateGuestAsync(Client, new
        {
            firstName = "Typed",
            lastName = "Client",
            email
        });

        created.Raw.ShouldBeCreated();
        // Mutations return the entity (internal setters → not STJ-deserializable): read raw JSON.
        var createdJson = System.Text.Json.JsonDocument.Parse(await created.ReadBodyAsync());
        var guestId = createdJson.RootElement.GetProperty("id").GetGuid();

        // DTO-returning endpoints deserialize through the typed response.
        var fetched = await Api.Guests.GetGuestAsync(Client, guestId);
        fetched.Raw.ShouldBeOk();
        (await fetched.ReadAsync()).Email.Should().Be(email);
    }

    [Fact]
    public async Task TypedClient_QueryParams_BuildUrlFromApiRoutes()
    {
        var response = await Api.Booking.GetSessionHintAsync(Client);

        response.Raw.ShouldBeOk();
        (await response.ReadAsync()).Should().Be("none");
    }

    [Fact]
    public void ApiRoutes_Constants_AreCompileTimeAndTyped()
    {
        // Tier 1 directly: constants + typed URL builders from the app assembly.
        Showcase.Booking.ApiRoutes.Booking.CreateGuestMethod.Should().Be("POST");

        var id = Guid.NewGuid();
        var url = Showcase.Booking.ApiRoutes.Guests.GetGuest(id);
        url.Should().Contain(id.ToString());
    }
}
