using System.Net;
using System.Text;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     W1 endpoint gaps: HttpVerb.Head (MapMethods, no body) and [MaxBodySize]
///     (413 via RequestLimitsStep before the body is read).
/// </summary>
public class HeadAndBodyLimitTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task HeadEndpoint_ReturnsNoContentWithoutBody()
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, "/api/reservations-availability");
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().BeEmpty("HEAD responses must not carry a body");
    }

    [Fact]
    public async Task HeadEndpoint_GetVerbOnSameRoute_IsNotMapped()
    {
        var response = await GetRawAsync("/api/reservations-availability");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MaxBodySize_SmallBody_Succeeds()
    {
        var response = await PostAsync("/api/booking-notes", new { text = "short note" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task MaxBodySize_OversizedBody_Returns413()
    {
        var oversized = new string('x', 1024);
        var response = await PostAsync("/api/booking-notes", new { text = oversized });

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }
}
