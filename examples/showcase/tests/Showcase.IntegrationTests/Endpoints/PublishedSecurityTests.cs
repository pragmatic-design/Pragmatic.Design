using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     How the published contract says to authenticate.
/// </summary>
/// <remarks>
///     <para>
///         A client generated from a document without a security scheme has no way to send a
///         credential to an API that requires one: it compiles, calls, gets 401, and the reason is in a
///         document that never named authentication.
///     </para>
///     <para>
///         ⚠️ The generator does not decide it. It knows <b>which</b> operations require
///         authentication — <c>[AllowAnonymous]</c>, at compile time — and cannot know <b>how</b>, because
///         that is chosen in <c>Program.cs</c>. Guessing «bearer» would be right for an entry point
///         that registers <c>JwtBearer</c> and wrong for a development handler or anything added later.
///         Whoever configures authentication registers an <c>IOpenApiSecuritySchemeContributor</c>, and
///         the wiring composes the two halves.
///     </para>
///     <para>
///         ⚠️ The two cases here are <b>the same endpoints with two configurations</b>: this showcase
///         takes the JWT branch only if <c>Jwt:Key</c> is set, otherwise it installs a no-op handler. The
///         pair is what makes the measure a measure — «publishes bearer» alone is satisfied by always
///         publishing it, which is exactly the defect to avoid.
///     </para>
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
public sealed class PublishedSecurityTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ShowcaseWebFactory _withJwt = null!;

    public Task InitializeAsync()
    {
        // Jwt:Key makes Program.cs take the UseJwtAuthentication branch instead of the no-op. Passed as
        // extraConfig because that value is read while PragmaticApp's callback runs, and only what goes
        // through UseSetting arrives there.
        _withJwt = new ShowcaseWebFactory(
            fixture,
            extraConfig: new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "a-signing-key-long-enough-for-hmac-sha256-in-a-test",
                ["Jwt:Issuer"] = "https://showcase.test",
                ["Jwt:Audience"] = "showcase-tests",
            });

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _withJwt.DisposeAsync().ConfigureAwait(false);

    [Fact]
    public async Task WithJwtConfigured_TheContractSaysBearer()
    {
        var doc = await TheDocumentAsync(_withJwt);

        var scheme = doc.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty("bearerAuth");

        scheme.GetProperty("type").GetString().Should().Be("http");
        scheme.GetProperty("scheme").GetString().Should().Be("bearer");
        scheme.GetProperty("bearerFormat").GetString().Should().Be("JWT");
        scheme.GetProperty("description").GetString().Should().Be("JWT issued by this application.",
            "the description comes from the package that configured authentication");

        doc.GetProperty("security").EnumerateArray()
            .SelectMany(entry => entry.EnumerateObject().Select(p => p.Name))
            .Should().Contain("bearerAuth",
                "declared at document level, it applies to every operation that does not opt out");
    }

    /// <summary>The control: authentication that does not describe itself is not invented.</summary>
    /// <remarks>
    ///     Without <c>Jwt:Key</c> this showcase installs <c>NoOpAuthenticationHandler</c> under a scheme
    ///     of its own, and nobody registers a contributor. The document then <b>stays silent</b>, and the
    ///     application writes it to the logs. Guessing would publish «bearer» with confidence, and a
    ///     generated client would send a credential nobody reads.
    /// </remarks>
    [Fact]
    public async Task WithoutIt_TheContractSaysNothingRatherThanGuessing()
    {
        await using var plain = new ShowcaseWebFactory(fixture);

        var components = (await TheDocumentAsync(plain)).GetProperty("components");

        components.TryGetProperty("securitySchemes", out _).Should().BeFalse(
            "nobody described how to authenticate, and a contract that guesses is worse than one "
            + "that stays silent: the error would surface downstream, in the integrator's application");
    }

    /// <summary>And an anonymous operation opts out, in both configurations.</summary>
    [Fact]
    public async Task AnAnonymousOperation_OptsOut()
    {
        var operation = (await TheDocumentAsync(_withJwt))
            .GetProperty("paths").GetProperty("/api/availability").GetProperty("get");

        operation.TryGetProperty("security", out var security).Should().BeTrue(
            "an [AllowAnonymous] operation must say it requires nothing");
        security.GetArrayLength().Should().Be(0,
            "the empty array is how OpenAPI says «this requires nothing», and it is the half that "
            + "makes the requirement declared on the document usable");
    }

    /// <summary>
    ///     The runtime document — <c>MapOpenApi</c> enriched by <c>AddPragmaticOpenApi</c> — says the
    ///     same thing as the compiled one.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The enrichment must not publish a <c>Bearer</c> scheme of its own, «the most common
    ///     choice», and require it on every protected operation whatever authentication the application
    ///     has.
    /// </remarks>
    [Fact]
    public async Task WithJwtConfigured_TheRuntimeContractSaysBearer_UnderTheNameItWasGiven()
    {
        var doc = await TheDocumentAsync(_withJwt, "/openapi/runtime.json");

        var schemes = doc.GetProperty("components").GetProperty("securitySchemes");
        var scheme = schemes.GetProperty("bearerAuth");
        scheme.GetProperty("type").GetString().Should().Be("http");
        scheme.GetProperty("scheme").GetString().Should().Be("bearer");
        schemes.TryGetProperty("Bearer", out _).Should().BeFalse("the guessed scheme is not published beside it");

        Requirements(doc, "/api/reservations/{id}/cancel", "post").Should().Equal(["bearerAuth"],
            "a protected operation requires the scheme the application described, and only that");
    }

    /// <summary>The control, on the runtime document: without a contributor, no invented scheme.</summary>
    [Fact]
    public async Task WithoutIt_TheRuntimeContractSaysNothingRatherThanGuessing()
    {
        await using var plain = new ShowcaseWebFactory(fixture);

        var doc = await TheDocumentAsync(plain, "/openapi/runtime.json");

        (doc.TryGetProperty("components", out var components)
         && components.TryGetProperty("securitySchemes", out var schemes)
         && schemes.EnumerateObject().Any())
            .Should().BeFalse("nobody described how to authenticate");
        Requirements(doc, "/api/reservations/{id}/cancel", "post").Should().BeEmpty(
            "an operation cannot require a scheme nobody described");
    }

    /// <summary>The names of the schemes an operation's security requirements cite.</summary>
    private static List<string> Requirements(JsonElement doc, string path, string method)
    {
        var operation = doc.GetProperty("paths").GetProperty(path).GetProperty(method);
        return operation.TryGetProperty("security", out var security)
            ? security.EnumerateArray().SelectMany(r => r.EnumerateObject().Select(p => p.Name)).ToList()
            : [];
    }

    private static async Task<JsonElement> TheDocumentAsync(ShowcaseWebFactory factory, string path = "/openapi/v1.json")
    {
        using var client = factory.CreateClient();

        // The compile-time document is exempt from tenant resolution; the runtime one is mapped by this
        // application with MapOpenApi, which is not, so this multi-tenant host wants a tenant for it.
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");

        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue(
            $"the document must be served, instead: {(int)response.StatusCode} {body}");

        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
