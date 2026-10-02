using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     Tests the [CreatedAt] location template (B20): a Create mutation annotated with
///     <c>[CreatedAt("/api/guests/{Id}")]</c> must return 201 with a real Location header pointing
///     at the created resource — and that URL must actually resolve.
/// </summary>
public class CreatedAtLocationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateGuest_Returns201_WithResolvableLocationHeader()
    {
        var response = await PostAsync("/api/guests", new
        {
            firstName = "Loc",
            lastName = "Ation",
            email = $"location-{Guid.NewGuid():N}@test.dev"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = body.GetProperty("id").GetGuid();

        response.Headers.Location.Should().NotBeNull("[CreatedAt] must fill the Location header");
        response.Headers.Location!.OriginalString.Should().Be($"/api/guests/{id}");

        // The advertised URL must resolve to the created resource.
        var fetched = await GetAsync<JsonElement>(response.Headers.Location.OriginalString);
        fetched.GetProperty("id").GetGuid().Should().Be(id);
    }
}
