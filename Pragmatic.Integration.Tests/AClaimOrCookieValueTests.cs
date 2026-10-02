using System.Net;
using System.Net.Http.Json;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests;

/// <summary>
///     Claims and cookies on <c>required</c> and <c>init</c> properties reach the operation, and the
///     refusals in front of them still refuse.
/// </summary>
/// <remarks>
///     The generated endpoint reads them before building the operation and sets them in its object
///     initializer. Compiling proves the shape; this proves what arrives.
/// </remarks>
public sealed class AClaimOrCookieValueTests
{
    private const string Session = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private static async Task<HttpResponseMessage> CallAsync(string? role, string? cookies)
    {
        await using var factory = new AuthenticatingTestFactory(endpointOptions: null);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/caller");
        request.Headers.Add(NoCredentialsAuthenticationHandler.UserHeader, "someone");
        if (role is not null)
            request.Headers.Add(NoCredentialsAuthenticationHandler.RolesHeader, role);
        if (cookies is not null)
            request.Headers.Add("Cookie", cookies);

        var response = await client.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();
        return response;
    }

    [Fact]
    public async Task SentValues_ReachTheOperation()
    {
        using var response = await CallAsync("admin", $"session={Session}; theme=dark; level=7");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<string>())
            .Should().Be("admin|someone|2|3f2504e04f8911d39a0c0305e82c3301|dark|7");
    }

    /// <summary>
    ///     The control for the optional values: absent, they keep their declaration — 2 and 3, not 0.
    /// </summary>
    [Fact]
    public async Task AbsentOptionalValues_LeaveTheDeclarationInPlace()
    {
        using var response = await CallAsync("admin", $"session={Session}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<string>())
            .Should().Be("admin|someone|2|3f2504e04f8911d39a0c0305e82c3301|none|3");
    }

    /// <summary>
    ///     The control for the reads moving ahead of the construction: an authenticated caller without the
    ///     required claim is still 401 — from the generated read, which names the claim, not from a challenge.
    /// </summary>
    [Fact]
    public async Task AMissingRequiredClaim_IsStillRefused()
    {
        using var response = await CallAsync(role: null, $"session={Session}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Missing required claim");
    }

    /// <summary>And a missing required cookie is still 400.</summary>
    [Fact]
    public async Task AMissingRequiredCookie_IsStillRefused()
    {
        using var response = await CallAsync("admin", cookies: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>And a malformed one is refused, not coerced to an empty Guid.</summary>
    [Fact]
    public async Task AMalformedRequiredCookie_IsStillRefused()
    {
        using var response = await CallAsync("admin", "session=not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
