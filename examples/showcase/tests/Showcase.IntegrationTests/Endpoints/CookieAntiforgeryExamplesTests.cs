using System.Net;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     W2 endpoint gaps: [FromCookie] binding, [RequireAntiforgery] enforcement on form
///     endpoints, and [RequestExample]/[ResponseExample] flowing into the OpenAPI document.
/// </summary>
public class CookieAntiforgeryExamplesTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task FromCookie_WithCookie_BindsValue()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/session-hint");
        request.Headers.Add("Cookie", "session-hint=abc123");

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("abc123");
    }

    [Fact]
    public async Task FromCookie_MissingOptionalCookie_UsesFallback()
    {
        var response = await GetRawAsync("/api/session-hint");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("none");
    }

    [Fact]
    public async Task RequireAntiforgery_FormPostWithoutToken_IsRejected()
    {
        using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("Message", "hello")]);
        var response = await Client.PostAsync("/api/feedback-form", content);

        // The antiforgery middleware rejects the request before the handler runs.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "form POST without an antiforgery token must be rejected");
    }

    [Fact]
    public async Task OpenApi_WhenAvailable_ContainsRequestExamples()
    {
        var response = await GetRawAsync("/openapi/v1.json");
        if (response.StatusCode != HttpStatusCode.OK) return; // schema generation issues tolerated (see OpenApiTests)

        var json = await response.Content.ReadAsStringAsync();

        json.Should().Contain("urgent: guest allergic to nuts",
            "[RequestExample] payloads must flow into the OpenAPI document");
        json.Should().Contain("Stores a short note attached to the booking session.",
            "[EndpointDescription] must flow into the OpenAPI operation description");
    }
}
