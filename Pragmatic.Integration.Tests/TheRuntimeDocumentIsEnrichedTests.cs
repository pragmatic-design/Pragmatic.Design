using System.Text.Json;
using Pragmatic.Abstractions.Http;
using Pragmatic.Endpoints.Configuration;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests;

/// <summary>
///     <c>AddPragmaticOpenApi()</c> enriches the runtime document from the manifest in a host with no
///     Composition — generated endpoints, <c>MapPragmaticEndpoints()</c>, nothing else.
/// </summary>
/// <remarks>
///     ⚠️ It enriched nothing. The enrichment reads the manifests registered at load time, and only the
///     aggregated manifest a Composition host generates registered itself: here the registry was
///     empty and the transformer returned on its first line. Each assertion below is
///     something only the manifest knows — the API explorer sees neither a cookie nor a default.
/// </remarks>
public sealed class TheRuntimeDocumentIsEnrichedTests
{
    [Fact]
    public async Task TheContributedScheme_IsDeclared()
    {
        using var document = await DocumentAsync();

        document.RootElement.TryGetProperty("components", out var components).Should().BeTrue(
            "the enrichment declares the contributed scheme under components");
        components.TryGetProperty("securitySchemes", out var schemes).Should().BeTrue(components.GetRawText());
        schemes.TryGetProperty("apiKey", out _).Should().BeTrue(schemes.GetRawText());
    }

    [Fact]
    public async Task ACookie_IsDescribed_FromTheManifest()
    {
        using var document = await DocumentAsync();

        var parameters = Operation(document, "/api/caller", "get").GetProperty("parameters");

        parameters.EnumerateArray()
            .Where(p => p.GetProperty("name").GetString() == "session")
            .Select(p => p.GetProperty("in").GetString())
            .Should().Equal("cookie");
    }

    [Fact]
    public async Task ADeclaredDefault_IsDescribed_FromTheManifest()
    {
        using var document = await DocumentAsync();

        var limit = Operation(document, "/api/notes/echo", "get").GetProperty("parameters")
            .EnumerateArray().Single(p => p.GetProperty("name").GetString() == "limit");

        limit.GetProperty("schema").TryGetProperty("default", out var value).Should().BeTrue(limit.GetRawText());
        value.GetInt32().Should().Be(20);
    }

    /// <summary>
    ///     The control: without <c>AddPragmaticOpenApi()</c> the document has no cookie on the same
    ///     operation, so the one above is the enrichment's.
    /// </summary>
    [Fact]
    public async Task WithoutTheEnrichment_TheCookieIsNotThere()
    {
        using var document = await DocumentAsync(enrich: false);

        var operation = Operation(document, "/api/caller", "get");
        var names = operation.TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList()
            : [];

        names.Should().NotContain("session");
    }

    private static async Task<JsonDocument> DocumentAsync(bool enrich = true)
    {
        await using var factory = new AuthenticatingTestFactory(
            new PragmaticEndpointsOptions(),
            publishOpenApi: true,
            services: services =>
            {
                if (!enrich)
                    return;

                services.AddPragmaticOpenApi();
                services.AddSingleton<IOpenApiSecuritySchemeContributor, ApiKeyScheme>();
            });
        using var client = factory.CreateClient();

        return JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
    }

    private static JsonElement Operation(JsonDocument document, string path, string method)
    {
        document.RootElement.GetProperty("paths").TryGetProperty(path, out var item)
            .Should().BeTrue($"'{path}' must be in the document");

        return item.GetProperty(method);
    }

    private sealed class ApiKeyScheme : IOpenApiSecuritySchemeContributor
    {
        public OpenApiSecurityScheme Describe() => new()
        {
            Name = "apiKey",
            Type = "apiKey",
            ParameterName = "X-Api-Key",
            In = "header",
        };
    }
}
