using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Showcase.BlazorClient;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     Exercises the SG-generated typed HTTP client (<see cref="IBookingClient"/>) against the running Showcase
///     app, over the real HTTP pipeline.
///     <para>
///         Elsewhere the client is only proven to <i>compile</i> — the consumer project registers it and prints
///         a line — and some defects only surface on the wire: a query parameter dropped from the generated
///         signature, or a GET carrying a query object issued as a POST (405). Each test below fails if either
///         happens.
///     </para>
/// </summary>
public class GeneratedClientTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private IBookingClient CreateTypedClient()
    {
        var services = new ServiceCollection();
        services.AddBookingClient("http://localhost");
        services.ConfigureHttpClientDefaults(builder =>
        {
            // Route the generated client at the in-memory test server, and give it the same identity the
            // rest of the suite uses (the endpoints are tenant-scoped and permission-gated).
            builder.ConfigurePrimaryHttpMessageHandler(CreateServerHandler);
            builder.ConfigureHttpClient(client =>
            {
                client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
                client.DefaultRequestHeaders.Add("X-User-Id", "test-user");
                client.DefaultRequestHeaders.Add("X-User-Name", "Generated Client Test");
                client.DefaultRequestHeaders.Add("X-User-Permissions", "catalog.*,booking.*");
            });
        });

        return services.BuildServiceProvider().GetRequiredService<IBookingClient>();
    }

    private static CreateGuestMutationRequest NewGuest(string email) => new()
    {
        FirstName = "Typed",
        LastName = "Client",
        Email = email,
        PreferredLanguage = "en"
    };

    [Fact]
    public async Task CreateAndGet_RoundTripsThroughTheGeneratedClient()
    {
        var client = CreateTypedClient();
        var email = $"roundtrip-{Guid.NewGuid():N}@example.com";

        var created = await client.CreateGuestMutation(NewGuest(email));

        created.IsSuccess.Should().BeTrue(created.IsSuccess ? "" : $"create failed: {created.Error.Title}");
        created.Value.Email.Should().Be(email);

        var fetched = await client.GetGuestQuery(created.Value.Id);

        fetched.IsSuccess.Should().BeTrue();
        fetched.Value.Id.Should().Be(created.Value.Id);
        fetched.Value.FullName.Should().Be("Typed Client");
    }

    /// <summary>
    ///     The regression that matters: a query parameter must reach the server and actually filter. When the
    ///     generator dropped query parameters the method took no arguments at all, so this could not even be
    ///     expressed — and any "search" call silently returned unfiltered data.
    /// </summary>
    [Fact]
    public async Task SearchGuests_FiltersByQueryParameter()
    {
        var client = CreateTypedClient();
        var mine = $"filter-{Guid.NewGuid():N}@example.com";
        var other = $"other-{Guid.NewGuid():N}@example.com";

        (await client.CreateGuestMutation(NewGuest(mine))).IsSuccess.Should().BeTrue();
        (await client.CreateGuestMutation(NewGuest(other))).IsSuccess.Should().BeTrue();

        var result = await client.SearchGuestsQuery(email: mine);

        result.IsSuccess.Should().BeTrue(result.IsSuccess ? "" : $"search failed: {result.Error.Title}");
        result.Value.Items.Should().OnlyContain(g => g.Email == mine,
            "the email query parameter must reach the server, not be dropped from the request");
        result.Value.Items.Should().ContainSingle();
    }

    /// <summary>A GET that carries a query object must stay a GET — issuing it as a POST returned 405.</summary>
    [Fact]
    public async Task SearchGuests_IsIssuedAsAGet_NotAPost()
    {
        var client = CreateTypedClient();

        var result = await client.SearchGuestsQuery(page: 1, pageSize: 5);

        result.IsSuccess.Should().BeTrue(
            result.IsSuccess ? "" : $"expected a successful GET but got {result.Error.Code}/{result.Error.Title}");
        result.Value.PageSize.Should().Be(5, "the paging values must travel in the query string");
    }

    [Fact]
    public async Task SearchAvailableRooms_AcceptsItsQueryParameters()
    {
        var client = CreateTypedClient();

        // Anonymous endpoint; the point is that the four parameters exist and are accepted on the wire.
        var result = await client.SearchAvailableRoomsEndpoint(
            propertyId: Guid.NewGuid(),
            checkIn: DateTimeOffset.UtcNow.AddDays(1),
            checkOut: DateTimeOffset.UtcNow.AddDays(3),
            guests: 2);

        result.IsSuccess.Should().BeTrue(
            result.IsSuccess ? "" : $"availability search failed: {result.Error.Code}/{result.Error.Title}");
    }

    [Fact]
    public async Task GetGuest_WithUnknownId_MapsToATypedError()
    {
        var client = CreateTypedClient();

        var result = await client.GetGuestQuery(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }
}
