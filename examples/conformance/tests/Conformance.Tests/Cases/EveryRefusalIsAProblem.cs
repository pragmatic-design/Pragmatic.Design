using System.Net;
using System.Text;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     Every API refusal travels as <c>application/problem+json</c>, whatever the code.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The OpenAPI document declares <c>problem+json</c> for every error, so the wire must answer it
///         for every code — the validation 422 and a query's 404 included, which are the codes a client sees
///         most often. The same ProblemDetails body as <c>application/json</c> would make contract and wire
///         disagree.
///     </para>
///     <para>
///         One case per family, each by its own road: binding, authentication, authorization, validation
///         and the query that finds nothing are five different places that write an error response.
///     </para>
/// </remarks>
public class EveryRefusalIsAProblem(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task ABodyThatCannotBeRead_400()
    {
        using var unreadable = new StringContent("{ \"externalId\": ", Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/conversion-subjects", unreadable);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be(ProblemJson);
    }

    [Fact]
    public async Task AnAnonymousCallerOnAProtectedRoute_401()
    {
        var response = await Client.GetAsync("/api/catalog-items");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be(ProblemJson);
    }

    [Fact]
    public async Task ACallerWithoutThePermission_403()
    {
        Client.DefaultRequestHeaders.Add("X-User-Id", "without-permission");
        Client.DefaultRequestHeaders.Add("X-User-Name", "without-permission");

        var response = await Client.GetAsync("/api/catalog-items");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be(ProblemJson);
    }

    [Fact]
    public async Task AQueryThatFindsNothing_404()
    {
        var response = await Client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be(ProblemJson);
    }

    [Fact]
    public async Task ARequestTheRulesReject_422()
    {
        var response = await PostAsync("/api/conversion-subjects", new
        {
            externalId = Guid.NewGuid().ToString(),
            isPriority = "true",
            quantity = "seven",
            code = 4711,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.Should().Be(ProblemJson);
    }
}
