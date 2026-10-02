using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Manifest.Models;
using Pragmatic.SourceGenerator.Features.Manifest.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Manifest;

/// <summary>
///     The manifest JSON must carry deprecation, rate-limit, cache, tags, and
///     file-upload metadata so OpenAPI generated from the manifest is complete.
/// </summary>
public class ManifestEndpointMetadataTests
{
    private static string RenderJson(ManifestEndpointModel endpoint)
    {
        var model = new ManifestModel
        {
            Assembly = "MyApp",
            SchemaVersion = "1.0.0",
            Endpoints = [endpoint]
        };

        return new ManifestJsonTemplate(model).RenderOutput().Text;
    }

    private static ManifestEndpointModel BaseEndpoint() => new()
    {
        OperationId = "Catalog.GetWidget",
        HttpMethod = "GET",
        FullRoute = "/widgets/{id}"
    };

    [Fact]
    public void RenderEndpoint_Deprecated_EmitsIsDeprecated()
    {
        var json = RenderJson(BaseEndpoint() with { IsDeprecated = true });

        json.Should().Contain("\"isDeprecated\": true");
    }

    [Fact]
    public void RenderEndpoint_NotDeprecated_OmitsIsDeprecated()
    {
        var json = RenderJson(BaseEndpoint());

        json.Should().NotContain("isDeprecated");
    }

    [Fact]
    public void RenderEndpoint_RateLimitPolicy_EmitsPolicyName()
    {
        var json = RenderJson(BaseEndpoint() with { RateLimitPolicy = "tight" });

        json.Should().Contain("\"rateLimitPolicy\": \"tight\"");
    }

    [Fact]
    public void RenderEndpoint_CacheDuration_EmitsSeconds()
    {
        var json = RenderJson(BaseEndpoint() with { CacheDurationSeconds = 60 });

        json.Should().Contain("\"cacheDurationSeconds\": 60");
    }

    [Fact]
    public void RenderEndpoint_Tags_EmitsTagArray()
    {
        var json = RenderJson(BaseEndpoint() with { Tags = ["catalog", "public"] });

        json.Should().Contain("\"tags\"");
        json.Should().Contain("\"catalog\"");
        json.Should().Contain("\"public\"");
    }

    [Fact]
    public void RenderEndpoint_FileUpload_EmitsConstraints()
    {
        var endpoint = BaseEndpoint() with
        {
            FileUpload = new ManifestFileUploadModel
            {
                MaxFileSizeBytes = 5_000_000L,
                AllowedContentTypes = ["image/png", "image/jpeg"]
            }
        };

        var json = RenderJson(endpoint);

        json.Should().Contain("\"fileUpload\"");
        json.Should().Contain("\"maxFileSizeBytes\": 5000000");
        json.Should().Contain("\"allowedContentTypes\"");
        json.Should().Contain("\"image/png\"");
        json.Should().Contain("\"image/jpeg\"");
    }

    [Fact]
    public void RenderEndpoint_NoExtraMetadata_OmitsAllOptionalFields()
    {
        var json = RenderJson(BaseEndpoint());

        json.Should().NotContain("rateLimitPolicy");
        json.Should().NotContain("cacheDurationSeconds");
        json.Should().NotContain("fileUpload");
    }
}
