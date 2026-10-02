using System.Text.Json;
using Pragmatic.Endpoints.Configuration;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests;

/// <summary>
///     The runtime OpenAPI document — <c>AddOpenApi()</c> and <c>MapOpenApi()</c>, the one Scalar,
///     Swagger UI and client generators read — describes the generated endpoints beside the
///     application's own.
/// </summary>
/// <remarks>
///     ⚠️ It described none of them. They are mapped as <c>RequestDelegate</c>s so that they survive an
///     AOT publish, and ASP.NET's API explorer describes an endpoint from its <c>MethodInfo</c>, which a
///     <c>RequestDelegate</c> does not have: the document held <c>/hand-written</c> and nothing else, on
///     a host answering <c>POST /api/orders</c>.
/// </remarks>
public sealed class TheRuntimeDocumentTests
{
    [Fact]
    public async Task ItListsTheGeneratedEndpoints_BesideTheApplicationsOwn()
    {
        using var document = await DocumentAsync(new PragmaticEndpointsOptions());

        var paths = Paths(document);

        paths.Should().Contain("/api/orders");
        paths.Should().Contain("/api/orders/{id}");
        paths.Should().Contain("/api/orders/{id}/notes");
        paths.Should().Contain("/hand-written", "the control: the application's own endpoint is still described");
    }

    /// <summary>
    ///     <c>EnableOpenApi = false</c> takes the generated endpoints out, and only those.
    /// </summary>
    [Fact]
    public async Task EnableOpenApiFalse_TakesTheGeneratedEndpointsOut()
    {
        using var document = await DocumentAsync(new PragmaticEndpointsOptions { EnableOpenApi = false });

        var paths = Paths(document);

        paths.Should().NotContain("/api/orders");
        paths.Should().NotContain("/api/orders/{id}");
        paths.Should().Contain("/hand-written", "the option is about Pragmatic's operations, not the document");
    }

    [Fact]
    public async Task EachParameter_IsDescribedWhereItIsBound_WithItsType()
    {
        using var document = await DocumentAsync(new PragmaticEndpointsOptions());

        var name = Parameter(Operation(document, "/api/orders", "post"), "name");
        name.GetProperty("in").GetString().Should().Be("query");
        name.GetProperty("schema").TryGetProperty("type", out var nameType).Should().BeTrue(name.GetRawText());
        nameType.GetString().Should().Be("string");

        var id = Parameter(Operation(document, "/api/orders/{id}", "get"), "id");
        id.GetProperty("in").GetString().Should().Be("path");
        id.GetProperty("schema").GetProperty("format").GetString().Should().Be("uuid",
            "the type is Guid, which the route text alone does not say");

        var notes = Operation(document, "/api/orders/{id}/notes", "post");

        var priority = Parameter(notes, "priority");
        priority.GetProperty("in").GetString().Should().Be("query");
        priority.TryGetProperty("required", out var priorityRequired).Should().BeTrue(priority.GetRawText());
        priorityRequired.GetBoolean().Should().BeTrue("the property is required, so the binding refuses without it");

        // The control: an optional value is not described as required.
        var source = Parameter(notes, "X-Note-Source");
        source.GetProperty("in").GetString().Should().Be("header");
        (source.TryGetProperty("required", out var sourceRequired) && sourceRequired.GetBoolean())
            .Should().BeFalse("the header is optional");
    }

    /// <summary>
    ///     A query value carrying Pragmatic.Validation's <c>[Required]</c> is described as required:
    ///     without it the request is refused with 422, and the contract said it could be left out.
    /// </summary>
    [Fact]
    public async Task AQueryValueCarryingRequired_IsDescribedAsRequired()
    {
        using var document = await DocumentAsync(new PragmaticEndpointsOptions());

        var operation = Operation(document, "/api/orders", "post");

        var name = Parameter(operation, "name");
        name.TryGetProperty("required", out var nameRequired).Should().BeTrue(name.GetRawText());
        nameRequired.GetBoolean().Should().BeTrue();

        // The control: amount carries no [Required], and stays optional.
        var amount = Parameter(operation, "amount");
        (amount.TryGetProperty("required", out var amountRequired) && amountRequired.GetBoolean())
            .Should().BeFalse("amount has no [Required]");
    }

    [Fact]
    public async Task TheBody_IsDescribedAsJson_WithItsShape()
    {
        using var document = await DocumentAsync(new PragmaticEndpointsOptions());

        var operation = Operation(document, "/api/orders/{id}/notes", "post");
        var schema = operation.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");

        Resolve(document, schema).GetProperty("properties").TryGetProperty("text", out _).Should().BeTrue(
            "the body carries the property no attribute places anywhere else");
    }

    [Fact]
    public async Task TheResponses_AreDescribedWithTheirStatus()
    {
        using var document = await DocumentAsync(new PragmaticEndpointsOptions());

        var responses = Operation(document, "/api/orders", "post").GetProperty("responses");

        responses.GetProperty("201").GetProperty("content").TryGetProperty("application/json", out _)
            .Should().BeTrue("a create answers 201 with the created resource");
        responses.GetProperty("400").GetProperty("content").TryGetProperty("application/problem+json", out _)
            .Should().BeTrue("every refusal is a problem");
    }

    private static async Task<JsonDocument> DocumentAsync(PragmaticEndpointsOptions options)
    {
        await using var factory = new AuthenticatingTestFactory(options, publishOpenApi: true);
        using var client = factory.CreateClient();

        return JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
    }

    private static List<string> Paths(JsonDocument document)
        => document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

    private static JsonElement Operation(JsonDocument document, string path, string method)
    {
        document.RootElement.GetProperty("paths").TryGetProperty(path, out var item)
            .Should().BeTrue($"'{path}' must be in the document");

        return item.GetProperty(method);
    }

    private static JsonElement Parameter(JsonElement operation, string name)
    {
        operation.TryGetProperty("parameters", out var parameters).Should().BeTrue("the operation has parameters");

        var found = parameters.EnumerateArray().Where(p => p.GetProperty("name").GetString() == name).ToList();
        found.Count.Should().Be(1, $"'{name}' is described once");

        return found[0];
    }

    /// <summary>Follows a <c>$ref</c> into <c>components/schemas</c>, or returns the schema itself.</summary>
    private static JsonElement Resolve(JsonDocument document, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
            return schema;

        var name = reference.GetString()!.Split('/')[^1];
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }
}
