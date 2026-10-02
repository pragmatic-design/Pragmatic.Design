using System.Net;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     The published OpenAPI document: served, and saying what it should.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ No case tolerates a 500 or returns early on one. A test that accepts both outcomes
///         cannot fail, and one that skips its assertions on the interesting path reports green about
///         a document it never read. The document is generated at compile time from the manifest, so
///         a 500 is not a condition to tolerate: it is the failure.
///     </para>
///     <para>
///         ⚠️ Nor does any case assert with <c>Contain("Invoices")</c> over the whole text. A tag, a
///         path segment and a schema name are all just letters in there, so such an assertion passes
///         on evidence it was not looking for — <c>Contain("404")</c> most of all, which any
///         identifier containing those digits satisfies. Every case here reads the parsed document and names the
///         place the value has to be in.
///     </para>
/// </remarks>
public class OpenApiTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task OpenApiSpec_IsServed()
    {
        var response = await GetRawAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the document is a compile-time constant served as it is; failing to produce it is a "
            + "defect, not a condition to tolerate");
    }

    [Fact]
    public async Task TheSummaryAndTags_AreOnTheOperationThatDeclaresThem()
    {
        var refund = (await TheDocumentAsync())
            .GetProperty("paths").GetProperty("/api/invoices/{id}/refund").GetProperty("post");

        refund.GetProperty("summary").GetString().Should().Be("Refund Invoice",
            "[EndpointSummary] lands on the operation, and the old assertion would have passed on "
            + "those words appearing anywhere in the document");

        refund.GetProperty("tags").EnumerateArray().Select(t => t.GetString())
            .Should().Contain("Invoices", "[Tags] groups the operation");
    }

    /// <summary>
    ///     A POST describes what it accepts, with a schema a reader can follow.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This document is written at compile time from the Pragmatic manifest — it says so in its
    ///         own description — so the request body is emitted by <c>OpenApiJsonGenerator</c> as a
    ///         <c>$ref</c> into <c>components/schemas</c>, named <c>{Operation}Request</c>. Asserting on
    ///         the <c>$ref</c> rather than on the word <c>requestBody</c> is what tells a described body
    ///         apart from an empty one.
    ///     </para>
    ///     <para>
    ///         ⚠️ Unlike its neighbours this one does not tolerate a 500. A document that cannot be
    ///         produced is not a reason to skip the assertion — it is the assertion failing.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task OpenApiSpec_DescribesTheRequestBodyOfAWrite()
    {
        var response = await GetRawAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a document that cannot be generated cannot be checked either");

        var json = await response.Content.ReadAsStringAsync();

        json.Should().Contain("#/components/schemas/CreateAmenityMutationRequest",
            "a write points at the schema of what it reads, not merely at the word requestBody");
        json.Should().Contain("\"CreateAmenityMutationRequest\"",
            "and the schema it points at is defined, or the $ref dangles");
    }

    /// <summary>The errors an operation can answer with are listed among its responses.</summary>
    /// <remarks>
    ///     ⚠️ Searching for <c>"404"</c> in the text of the whole document would find three digits any
    ///     identifier can contain, and pass without looking at any operation's responses. Here the
    ///     question is asked where the answer means something.
    /// </remarks>
    [Fact]
    public async Task TheErrorCodes_AreDeclaredAmongTheResponsesOfTheOperation()
    {
        var refund = (await TheDocumentAsync())
            .GetProperty("paths").GetProperty("/api/invoices/{id}/refund").GetProperty("post");

        var codes = refund.GetProperty("responses").EnumerateObject().Select(p => p.Name).ToList();

        codes.Should().Contain("404",
            "the operation declares a NotFoundError, and a generated client must know to expect it");
        codes.Should().Contain("422",
            "and a request that was read and refused by the rules answers 422");
        codes.Should().NotContain("200",
            "a refund answers 201: there is a single success code and that is it");
    }

    /// <summary>
    ///     The runtime document — <c>MapOpenApi</c>, which this host serves in Development — is built with
    ///     every endpoint shape this application has, and describes the generated ones.
    /// </summary>
    /// <remarks>
    ///     It described none of them: a generated endpoint is a <c>RequestDelegate</c>, which
    ///     ASP.NET's explorer skips. The shapes named here are the ones a hand-built description can get
    ///     wrong without failing a smaller host: a file upload, a form of plain fields, and one operation
    ///     mapped once per API version. The document service throws while it is being built when a
    ///     description lacks what it reads, so the status is the first assertion.
    /// </remarks>
    [Fact]
    public async Task TheRuntimeDocument_DescribesTheGeneratedEndpoints()
    {
        var paths = (await TheDocumentAsync("/openapi/runtime.json")).GetProperty("paths");

        var upload = paths.GetProperty("/api/properties/{propertyId}/photos").GetProperty("post");
        upload.GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _)
            .Should().BeTrue("the photo is uploaded as multipart");

        paths.GetProperty("/api/feedback-form").GetProperty("post").TryGetProperty("requestBody", out _)
            .Should().BeTrue("a form of plain fields is a request body too");

        paths.TryGetProperty("/api/reservations", out var reservations).Should().BeTrue(
            "an operation mapped once per API version is still one path");
        reservations.TryGetProperty("post", out _).Should().BeTrue();
    }

    private async Task<JsonElement> TheDocumentAsync(string path = "/openapi/v1.json")
    {
        var response = await GetRawAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the document must be served, instead: {(int)response.StatusCode} {body}");

        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
