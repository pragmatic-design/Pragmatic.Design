using System.Text.Json;
using Pragmatic.Endpoints.Configuration;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests.Unions;

/// <summary>
///     An operation whose error type is a C# 15 union documents each case, as the arity of the same errors does (#866).
/// </summary>
/// <remarks>
///     <para>
///         Read from the host's own OpenAPI document, which is built from the endpoint's metadata; the endpoint manifest and the generated
///         client read the same error list. Before #866 the union was documented as one error type and fell back to 400, while at run time
///         each case answered with its own status (<see cref="AUnionErrorSetAnswersWithItsCaseTests" />).
///     </para>
///     <para>
///         The control is <see cref="PlaceOrderArityAction" />, the same three errors as an arity: whatever the union documents, this is what
///         it must document.
///     </para>
/// </remarks>
public sealed class AUnionErrorSetIsDocumentedByItsCasesTests
{
    [Fact]
    public async Task TheUnionEndpoint_DocumentsTheResponsesOfTheArity()
    {
        using var document = await DocumentAsync();

        var union = Statuses(document, "/api/union-orders");
        var arity = Statuses(document, "/api/arity-orders");

        arity.Should().Contain(["404", "409", "423"], "the control: each error documented with its own status");
        union.Should().Contain(["404", "409", "423"], "each case of the union, documented as it is as a plain error type");
        union.Should().Equal(arity, "the union and the arity of the same errors are the same contract");
    }

    private static async Task<JsonDocument> DocumentAsync()
    {
        await using var factory = new AuthenticatingTestFactory(new PragmaticEndpointsOptions(), publishOpenApi: true);
        using var client = factory.CreateClient();

        return JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
    }

    private static List<string> Statuses(JsonDocument document, string path)
    {
        document.RootElement.GetProperty("paths").TryGetProperty(path, out var item).Should().BeTrue($"'{path}' must be in the document");

        return item.GetProperty("post").GetProperty("responses").EnumerateObject().Select(r => r.Name).Order(StringComparer.Ordinal).ToList();
    }
}
