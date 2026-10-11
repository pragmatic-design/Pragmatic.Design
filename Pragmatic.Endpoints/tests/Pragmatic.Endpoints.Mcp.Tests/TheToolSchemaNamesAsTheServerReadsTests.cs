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
}
