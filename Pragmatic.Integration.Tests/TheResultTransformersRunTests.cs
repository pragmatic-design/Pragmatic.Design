using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Pragmatic.Endpoints.Configuration;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Result.AspNetCore.OpenApi;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests;

/// <summary>
///     <c>AddResultTypeSupport()</c> runs in the runtime document, on every target this suite runs on (#832).
/// </summary>
/// <remarks>
///     On net11.0 the host has ASP.NET Core 11's OpenAPI package, built on Microsoft.OpenApi 3. A transformer compiled
///     against Microsoft.OpenApi 2 failed there with a <c>MissingMethodException</c> thrown from
///     <c>ResultOpenApiTransformer.IsResultTypeResponse</c>, and the document answered 500: nothing warned at build time,
///     and only a request for the document found it. That failure, with this very registration, is also what shows the
///     test reaches the transformer: a document served without it would prove nothing.
/// </remarks>
public sealed class TheResultTransformersRunTests
{
    [Fact]
    public async Task TheDocument_IsServed_WithTheResultTransformers()
    {
        await using var factory = new AuthenticatingTestFactory(
            new PragmaticEndpointsOptions(),
            publishOpenApi: true,
            services: services => services.ConfigureAll<OpenApiOptions>(options => options.AddResultTypeSupport()));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        var body = await response.Content.ReadAsStringAsync();

        ((int)response.StatusCode).Should().Be(200, body.Length > 600 ? body[..600] : body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("paths").EnumerateObject().Should().NotBeEmpty(
            "the transformers read the operations, so there must be some to read");
    }
}
