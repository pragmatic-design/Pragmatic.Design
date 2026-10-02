using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Endpoints.Configuration;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     <c>EnableOpenApi = false</c> leaves the Pragmatic endpoints out of the compile-time document that
///     <c>MapPragmaticOpenApi</c> serves.
/// </summary>
/// <remarks>
///     <para>
///         That document is the only one that lists them. A runtime document (<c>MapOpenApi</c>) does
///         not describe the generated endpoints at all — they are mapped as <c>RequestDelegate</c>s,
///         which the API explorer skips — so the option could only ever be observed here. Measured in
///         <c>Pragmatic.Integration.Tests.EndpointOptionsTests</c>.
///     </para>
///     <para>
///         ⚠️ <c>PragmaticOpenApiRegistry</c> is process-wide, so this class is in
///         <see cref="TheProcessWideOpenApiRegistryCollection" /> with every other class that registers
///         a document. Keeping them in one class was the earlier answer and it stopped being true as
///         soon as a second class needed registrations of its own — classes run in parallel.
///     </para>
/// </remarks>
[Collection(TheProcessWideOpenApiRegistryCollection.Name)]
public sealed class ThePublishedContractHonoursEnableOpenApiTests
{
    private const string Document = """
        {
          "openapi": "3.1.0",
          "info": { "title": "Orders", "version": "1.0.0" },
          "paths": { "/api/orders": { "get": { "operationId": "Orders.List" } } },
          "tags": [ { "name": "Orders" } ],
          "components": { "schemas": { "OrderDto": { "type": "object" } } }
        }
        """;

    [Fact]
    public async Task EnableOpenApiFalse_ServesTheDocumentWithNoOperations()
    {
        var document = await PublishedAsync(new PragmaticEndpointsOptions { EnableOpenApi = false });

        document.GetProperty("paths").EnumerateObject().Should().BeEmpty(
            "the Pragmatic endpoints are what this document describes, and the option leaves them out");
        document.TryGetProperty("tags", out _).Should().BeFalse("the tags existed only to group those operations");
        document.GetProperty("components").TryGetProperty("schemas", out _).Should().BeFalse(
            "and so did the schemas their bodies used");
        document.GetProperty("info").GetProperty("title").GetString().Should().Be("Orders",
            "the document is still served, so the route does not start answering 404");
    }

    /// <summary>The control: by default the operations are published.</summary>
    [Fact]
    public async Task ByDefault_ServesTheOperations()
    {
        var document = await PublishedAsync(new PragmaticEndpointsOptions());

        document.GetProperty("paths").EnumerateObject().Select(p => p.Name).Should().Contain("/api/orders");
        document.GetProperty("components").GetProperty("schemas").TryGetProperty("OrderDto", out _).Should().BeTrue();
    }

    private static async Task<JsonElement> PublishedAsync(PragmaticEndpointsOptions options)
    {
        PragmaticOpenApiRegistry.Register(Document);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);

        var app = builder.Build();
        try
        {
            app.MapPragmaticOpenApi();
            await app.StartAsync().ConfigureAwait(false);

            using var client = app.GetTestClient();
            using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json").ConfigureAwait(false));
            return json.RootElement.Clone();
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }
}
