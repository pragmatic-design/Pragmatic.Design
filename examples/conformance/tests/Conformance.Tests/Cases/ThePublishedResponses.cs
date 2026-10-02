using System.Net;
using System.Text;
using System.Text.Json;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     The codes the document declares, compared with those the API answers.
/// </summary>
/// <remarks>
///     <para>
///         A contract that promises less than the API does sends the generated client to handle a code it
///         does not expect; one that promises more makes it write dead branches. Both are found only by
///         comparing the two, and that is what this file does: it asks the API first, then reads the
///         document.
///     </para>
///     <para>
///         ⚠️ The order matters. Measuring the document first and then looking for a case that confirms it
///         is the way to write a test that always passes — the document would become its own proof.
///     </para>
/// </remarks>
public class ThePublishedResponses(PostgresFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>A failed validation: the API says 422, and the document must say it.</summary>
    /// <remarks>
    ///     ⚠️ 400 is for a body that could not be read, 422 for one that was read and refused: a document
    ///     that declared only 400 would describe a time when validation answered 400.
    /// </remarks>
    [Fact]
    public async Task AMutationThatRejects_DeclaresTheCodeItAnswers()
    {
        var response = await PostAsync("/api/conversion-subjects", new
        {
            externalId = Guid.NewGuid().ToString(),
            isPriority = "true",
            quantity = "seven",
            code = 4711,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "a request read and refused by the rules is 422");

        (await DeclaredCodesAsync("/api/conversion-subjects", "post")).Should().Contain("422",
            "the document is what a client is generated from, and it must name the code that arrives");
    }

    /// <summary>
    ///     The same 422, and the media type: what arrives is one of those the document declares.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A document declaring <c>application/problem+json</c> while the wire answers
    ///     <c>application/json</c> leaves a generated client that picks the deserializer by media type
    ///     without a branch for the response it receives.
    /// </remarks>
    [Fact]
    public async Task AMutationThatRejects_AnswersTheMediaTypeItDeclares()
    {
        var response = await PostAsync("/api/conversion-subjects", new
        {
            externalId = Guid.NewGuid().ToString(),
            isPriority = "true",
            quantity = "seven",
            code = 4711,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var onTheWire = response.Content.Headers.ContentType?.MediaType;

        (await DeclaredMediaTypesAsync("/api/conversion-subjects", "post", "422")).Should().Contain(onTheWire!,
            "the document must declare the media type the wire uses for that code");
    }

    /// <summary>
    ///     A Create that returns the key: the document describes the 201 with the shape the wire carries.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The generator writes the response record, so the compilation on which the manifest resolves
    ///     types does not contain it: without a description the document would name a schema nobody
    ///     defines.
    /// </remarks>
    [Fact]
    public async Task AnIdCreate_PublishesTheShapeItAnswers()
    {
        var response = await PostAsync("/api/shipment-ids", new
        {
            trackingCode = $"DOC-{Guid.NewGuid():N}"[..16],
            carrier = "ParcelCo",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var onTheWire = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .EnumerateObject().Select(p => p.Name).ToList();

        var document = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json")).RootElement;
        var schema = document.GetProperty("paths").GetProperty("/api/shipment-ids").GetProperty("post")
            .GetProperty("responses").GetProperty("201").GetProperty("content")
            .EnumerateObject().First().Value.GetProperty("schema");
        if (schema.TryGetProperty("$ref", out var reference))
            schema = document.GetProperty("components").GetProperty("schemas")
                .GetProperty(reference.GetString()!.Split('/').Last());

        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList()
            .Should().BeEquivalentTo(onTheWire, "the document publishes the shape the 201 really carries");
    }

    /// <summary>A body that could not be read: the API says 400, and the document must say it.</summary>
    /// <remarks>
    ///     The other half of the distinction. The case above proves the 422; this one sends malformed JSON
    ///     — received, not understood — and measures that the response is 400 and not 422, and that the
    ///     document declares that code.
    /// </remarks>
    [Fact]
    public async Task ABodyThatCannotBeRead_DeclaresTheCodeItAnswers()
    {
        using var unreadable = new StringContent("{ \"externalId\": ", Encoding.UTF8, "application/json");
        var response = await Client.PostAsync("/api/conversion-subjects", unreadable);
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"a body that could not be read is 400, not 422. Response: {text}");

        (await DeclaredCodesAsync("/api/conversion-subjects", "post")).Should().Contain("400",
            "the document must also name the code of the unreadable body");
    }

    /// <summary>A query that finds nothing: the API says 404, and the document must say it.</summary>
    /// <remarks>
    ///     ⚠️ <c>ProblemStatusCodes</c> must be populated for every operation, not only for mutations:
    ///     otherwise queries, domain actions and standalone endpoints publish only the success, and a
    ///     client generated from that document treats a 404 as an unexpected response.
    /// </remarks>
    [Fact]
    public async Task AQueryThatFindsNothing_DeclaresTheCodeItAnswers()
    {
        var response = await Client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a row that is not there is 404");

        (await DeclaredCodesAsync("/api/orders/{id}", "get")).Should().Contain("404",
            "it holds for a query exactly as for a mutation: it is the same pipeline");
    }

    /// <summary>And what the document promises in excess must be removed, not left.</summary>
    /// <remarks>
    ///     The control of the assertion above. Without it, «declares everything needed» is satisfied by
    ///     listing every existing code, which is a contract without content: a client generated from such
    ///     a document handles ten branches of which nine never arrive.
    /// </remarks>
    [Fact]
    public async Task AnAnonymousOperation_DoesNotPromiseAnAuthenticationFailure()
    {
        var codes = await DeclaredCodesAsync("/api/conversion-subjects", "post");

        codes.Should().NotContain("401",
            "the mutation is [AllowAnonymous]: no request is ever refused for authentication");
        codes.Should().NotContain("403",
            "nor for permissions, which is the same reason");
    }

    private async Task<List<string>> DeclaredCodesAsync(string path, string method)
    {
        var document = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json")).RootElement;

        return document
            .GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses")
            .EnumerateObject().Select(p => p.Name).ToList();
    }

    private async Task<List<string>> DeclaredMediaTypesAsync(string path, string method, string status)
    {
        var document = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json")).RootElement;

        return document
            .GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses")
            .GetProperty(status).GetProperty("content")
            .EnumerateObject().Select(p => p.Name).ToList();
    }
}
