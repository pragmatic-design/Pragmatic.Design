using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     An application host's CORS origins are <c>Cors:Origins</c> in its configuration (#748): a preflight from a listed origin is allowed,
///     one from another origin is not.
/// </summary>
/// <remarks>
///     <para>
///         <c>CorsStep</c> applied a default policy that no package registered, and only the gateway read its origins: a browser on another
///         origin got no <c>Access-Control-Allow-Origin</c> from an application host whatever its configuration said.
///     </para>
///     <para>
///         The control is the other origin: a policy that allowed every origin would satisfy the first case alone. A host of its own, because
///         the configuration is the subject.
///     </para>
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TheHostsCorsOriginsComeFromConfigurationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Allowed = "https://app.example.com";
    private ShowcaseWebFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new ShowcaseWebFactory(fixture, new Dictionary<string, string?> { ["Cors:Origins:0"] = Allowed });
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task APreflightFromAConfiguredOrigin_IsAllowed()
    {
        using var response = await _client.SendAsync(Preflight(Allowed));

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins).Should().BeTrue(
            "the origin is in Cors:Origins, so the host's default policy allows it");
        origins!.Should().ContainSingle().Which.Should().Be(Allowed);
    }

    [Fact]
    public async Task APreflightFromAnotherOrigin_IsNotAllowed()
    {
        using var response = await _client.SendAsync(Preflight("https://elsewhere.example.org"));

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse("the origin is not in Cors:Origins");
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/properties");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return request;
    }
}
