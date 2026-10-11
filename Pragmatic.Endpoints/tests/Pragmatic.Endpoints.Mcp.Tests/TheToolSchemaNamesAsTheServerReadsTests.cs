using System.Text.Json;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Mcp.Tests;

/// <summary>
///     A tool's input schema names a body property as the server's JSON writes it.
/// </summary>
/// <remarks>
///     The host's policy is <c>JsonNamingPolicy.CamelCase</c>, which lowers the whole leading acronym:
///     <c>URL</c> is <c>url</c>, <c>IOStream</c> is <c>ioStream</c>. A schema that lowers only the first
///     letter asks an agent for <c>uRL</c>, a field the contract, the document and every other client call
///     <c>url</c> (#146).
/// </remarks>
public class TheToolSchemaNamesAsTheServerReadsTests
{
    [Fact]
    public void AnAcronymLedBodyProperty_IsNamedAsTheHostsPolicyNamesIt()
    {
        var endpoint = new ManifestEndpoint
        {
            RequestBody = new ManifestBody
            {
                Properties =
                [
                    new ManifestProperty { Name = "URL", Type = "string", IsRequired = true },
                    new ManifestProperty { Name = "IOStream", Type = "string" },
                ],
            },
        };

        using var schema = JsonDocument.Parse(McpJsonSchemaBuilder.Build(endpoint));

        schema.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name)
            .Should().Equal(JsonNamingPolicy.CamelCase.ConvertName("URL"), JsonNamingPolicy.CamelCase.ConvertName("IOStream"));
        schema.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("url");
    }

    /// <summary>
    ///     A body property renamed with <c>[JsonPropertyName]</c> is asked for under its wire name, which the
    ///     manifest carries as <c>wireName</c> (#180).
    /// </summary>
    [Fact]
    public void ARenamedBodyProperty_IsNamedByItsWireName()
    {
        // Read as the manifest reader reads it, so the test exercises the manifest's own key.
        var endpoint = JsonSerializer.Deserialize<ManifestEndpoint>("""
            { "requestBody": { "properties": [
                { "name": "Name", "wireName": "label", "type": "string", "isRequired": true },
                { "name": "Weight", "type": "int" } ] } }
            """, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        using var schema = JsonDocument.Parse(McpJsonSchemaBuilder.Build(endpoint));

        schema.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name)
            .Should().Equal("label", "weight");
        schema.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("label");
    }
}
