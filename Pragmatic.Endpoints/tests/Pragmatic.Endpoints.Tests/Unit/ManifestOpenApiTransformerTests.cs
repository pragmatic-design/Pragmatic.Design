using Pragmatic.Testing.Assertions;
using Microsoft.OpenApi;
using Pragmatic.Endpoints.OpenApi;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     OpenAPI manifest enrichment correctness.
///     (1) Permission descriptions must be applied even for endpoints with no documented errors.
///     (2) Manifest routes must be combined with the runtime route prefix before matching.
/// </summary>
public class ManifestOpenApiTransformerTests
{
    [Fact]
    public void EnrichOperation_EndpointWithoutErrors_StillAddsPermissionDescription()
    {
        var operation = new OpenApiOperation();
        var endpoint = new ManifestEndpoint
        {
            HttpMethod = "GET",
            FullRoute = "/admin/settings",
            // No Errors — must not cause an early return that skips permission enrichment.
            Authorization = new ManifestAuth { RequiredPermissions = ["admin.read"] }
        };

        ManifestOpenApiTransformer.EnrichOperation(operation, endpoint, new OpenApiDocument(), []);

        operation.Description.Should().Contain("Requires permissions: admin.read");
    }

    [Fact]
    public void EnrichOperation_EndpointWithErrorsAndPermissions_AddsBoth()
    {
        var operation = new OpenApiOperation();
        var endpoint = new ManifestEndpoint
        {
            HttpMethod = "POST",
            FullRoute = "/orders",
            Errors = [new ManifestError { StatusCode = 404, Code = "NOT_FOUND" }],
            Authorization = new ManifestAuth { RequiredPermissions = ["orders.create"] }
        };

        ManifestOpenApiTransformer.EnrichOperation(operation, endpoint, new OpenApiDocument(), []);

        operation.Responses.Should().ContainKey("404");
        operation.Description.Should().Contain("Requires permissions: orders.create");
    }

    [Fact]
    public void EnrichOperation_ManifestTags_AreMergedWithoutDuplicates()
    {
        // ASP.NET already added "Orders" (different casing); manifest adds "Orders" + "Admin".
        var operation = new OpenApiOperation
        {
            Tags = new HashSet<OpenApiTagReference> { new("orders") }
        };
        var endpoint = new ManifestEndpoint
        {
            HttpMethod = "GET",
            FullRoute = "/orders",
            Tags = ["Orders", "Admin"]
        };

        ManifestOpenApiTransformer.EnrichOperation(operation, endpoint, new OpenApiDocument(), []);

        operation.Tags!.Select(t => t.Name).Should().BeEquivalentTo("orders", "Admin");
    }

    private static readonly Abstractions.Http.OpenApiSecurityScheme ApiKey = new()
    {
        Name = "apiKey",
        Type = "apiKey",
        ParameterName = "X-Api-Key",
        In = "header",
    };

    private static ManifestEndpoint ProtectedEndpoint => new()
    {
        HttpMethod = "POST",
        FullRoute = "/orders",
        Authorization = new ManifestAuth
        {
            AllowAnonymous = false,
            RequiredPermissions = ["orders.create"]
        }
    };

    /// <summary>
    ///     ⚠️ Inverted, not deleted: this pinned a <c>Bearer</c> JWT scheme the enrichment added on its
    ///     own. It declares what the application contributed now, and nothing else.
    /// </summary>
    [Fact]
    public void AddSecuritySchemes_DeclaresTheContributedScheme_AndNoOther()
    {
        var document = new OpenApiDocument();

        ManifestOpenApiTransformer.AddSecuritySchemes(document, [ApiKey]);

        document.Components!.SecuritySchemes!.Keys.Should().BeEquivalentTo("apiKey");
        var scheme = document.Components.SecuritySchemes["apiKey"];
        scheme.Type.Should().Be(SecuritySchemeType.ApiKey);
        scheme.Name.Should().Be("X-Api-Key");
        scheme.In.Should().Be(ParameterLocation.Header);
    }

    /// <summary>⚠️ Inverted, not deleted: the requirement names the contributed scheme, not "Bearer".</summary>
    [Fact]
    public void EnrichOperation_AuthenticatedEndpoint_RequiresTheContributedScheme()
    {
        var operation = new OpenApiOperation();

        ManifestOpenApiTransformer.EnrichOperation(operation, ProtectedEndpoint, new OpenApiDocument(), [ApiKey]);

        operation.Security.Should().ContainSingle();
        operation.Security![0].Keys.Select(k => k.Reference!.Id).Should().BeEquivalentTo("apiKey");
    }

    /// <summary>The control: with no contributed scheme, a protected operation names none.</summary>
    [Fact]
    public void EnrichOperation_AuthenticatedEndpoint_WithNoScheme_RequiresNothing()
    {
        var operation = new OpenApiOperation();

        ManifestOpenApiTransformer.EnrichOperation(operation, ProtectedEndpoint, new OpenApiDocument(), []);

        operation.Security.Should().BeEmpty("a requirement cannot name a scheme nobody described");
    }

    [Fact]
    public void EnrichOperation_AnonymousEndpoint_HasNoSecurityRequirement()
    {
        var operation = new OpenApiOperation();
        var endpoint = new ManifestEndpoint
        {
            HttpMethod = "GET",
            FullRoute = "/public/health",
            Authorization = new ManifestAuth { AllowAnonymous = true }
        };

        ManifestOpenApiTransformer.EnrichOperation(operation, endpoint, new OpenApiDocument(), []);

        operation.Security.Should().BeEmpty();
    }

    [Fact]
    public void EnrichOperation_ErrorResponse_HasProblemJsonSchema()
    {
        var operation = new OpenApiOperation();
        var endpoint = new ManifestEndpoint
        {
            HttpMethod = "POST",
            FullRoute = "/orders",
            Errors =
            [
                new ManifestError
                {
                    StatusCode = 409,
                    Code = "CONFLICT",
                    Extensions = [new ManifestErrorExt { Name = "conflictingId", Type = "int" }]
                }
            ]
        };

        ManifestOpenApiTransformer.EnrichOperation(operation, endpoint, new OpenApiDocument(), []);

        var response = operation.Responses!["409"];
        response.Content.Should().ContainKey("application/problem+json");
        var schema = response.Content!["application/problem+json"].Schema!;
        schema.Type.Should().Be(JsonSchemaType.Object);
        schema.Properties.Should().ContainKeys("type", "title", "status", "detail", "instance", "code");
        // Extension property is projected into the schema.
        schema.Properties.Should().ContainKey("conflictingId");
    }

    [Theory]
    [InlineData("", "/users/{id}", "/users/{id}")]
    [InlineData("/api", "/users/{id}", "/api/users/{id}")]
    [InlineData("api/", "/users", "api/users")]
    [InlineData("/api/v1/", "orders", "/api/v1/orders")]
    public void CombineRoutePrefix_PrependsRuntimePrefix(string prefix, string route, string expected)
    {
        ManifestOpenApiTransformer.CombineRoutePrefix(prefix, route).Should().Be(expected);
    }
}
