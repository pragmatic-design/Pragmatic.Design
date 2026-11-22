using System.Net;
using System.Text.Json;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A paged query's 200 carries the page and nothing else, and the document describes that page (#939).
/// </summary>
/// <remarks>
///     <para>
///         The outcome of the query is the status code. A body that also said <c>isSuccess</c> and
///         <c>isFailure</c> repeated it, and a client written against the JSON came to depend on members the
///         document did not name.
///     </para>
///     <para>
///         ⚠️ The wire is read first and the document after, as in <see cref="ThePublishedResponses"/>: the
///         document must follow the response, not be its own proof.
///     </para>
/// </remarks>
public class APageOnTheWire(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private static readonly string[] ThePage =
        ["items", "totalCount", "page", "pageSize", "totalPages", "hasPreviousPage", "hasNextPage", "skip"];

    [Fact]
    public async Task APage_CarriesThePageAndNotTheOutcome()
    {
        var onTheWire = await MembersOnTheWireAsync();

        onTheWire.Should().BeEquivalentTo(ThePage,
            "a 200 already says the query succeeded; the body is the page");
    }

    [Fact]
    public async Task APage_IsPublishedWithTheMembersItCarries()
    {
        var onTheWire = await MembersOnTheWireAsync();

        var document = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json")).RootElement;
        var schema = document.GetProperty("paths").GetProperty("/api/categories/paged").GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content")
            .EnumerateObject().First().Value.GetProperty("schema");
        if (schema.TryGetProperty("$ref", out var reference))
            schema = document.GetProperty("components").GetProperty("schemas")
                .GetProperty(reference.GetString()!.Split('/').Last());

        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList()
            .Should().BeEquivalentTo(onTheWire, "a client is generated from the document, and reads the wire");
    }

    private async Task<List<string>> MembersOnTheWireAsync()
    {
        var response = await Client.GetAsync("/api/categories/paged?page=1&pageSize=5");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return JsonDocument.Parse(body).RootElement.EnumerateObject().Select(p => p.Name).ToList();
    }
}
