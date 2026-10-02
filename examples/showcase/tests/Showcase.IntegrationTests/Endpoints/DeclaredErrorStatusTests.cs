using System.Net;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     What an operation can fail with, and whether the OpenAPI document says so.
/// </summary>
/// <remarks>
///     <para>
///         Each test here asserts the document <b>and</b> the behaviour, in that order, because either
///         alone is worthless: a document listing a status nothing produces is a lie, and a status
///         produced by a route that never declares it is a client written against the wrong contract.
///     </para>
///     <para>
///         No <c>if (response is not OK) return;</c>. The other OpenAPI tests in this suite have one,
///         which makes them pass whenever generation breaks — the exact shape of a test that reports
///         success by never running.
///     </para>
/// </remarks>
public class DeclaredErrorStatusTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>
    ///     A unique key behind an entity means every write on it can answer 409 — and now says so.
    /// </summary>
    /// <remarks>
    ///     Measured first: posting the same <c>[LogicKey]</c> twice answered 409 while the document
    ///     listed 400, 401, 403 and 500. The status was reachable, documented nowhere, and no test
    ///     noticed because nothing compared the two.
    /// </remarks>
    [Fact]
    public async Task ADuplicateLogicKey_Answers409_AndTheDocumentSaysSo()
    {
        var declared = await StatusesFor("/api/properties", "post");
        declared.Should().Contain(409,
            "Property has a [LogicKey], so a unique index can refuse the write");

        var body = new
        {
            code = $"DUP-{Guid.NewGuid():N}"[..12],
            name = "Duplicate Probe",
            city = "Rome",
            country = "IT",
            starRating = 3,
        };

        (await PostAsync("/api/properties", body)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostAsync("/api/properties", body)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    ///     An error declared on the mutation's base type reaches the document as its status.
    /// </summary>
    /// <remarks>
    ///     <c>CancelReservationMutation : Mutation&lt;Reservation, ConflictError&gt;</c> — the arity is
    ///     what carries the fact, and nothing else in the pipeline would know that cancelling can
    ///     conflict.
    /// </remarks>
    [Fact]
    public async Task AnErrorDeclaredOnTheMutation_IsListedAsItsStatus()
    {
        var declared = await StatusesFor("/api/reservations/{id}/cancel", "post");

        declared.Should().Contain(409, "the mutation declares ConflictError");
        declared.Should().Contain(404, "a load-mode mutation answers 404 when the row is not there");
    }

    /// <summary>
    ///     The scaffolded writes declare the same statuses, derived from the entity rather than written.
    /// </summary>
    [Fact]
    public async Task TheScaffoldedWrites_DeclareWhatTheEntityMakesPossible()
    {
        var create = await StatusesFor("/api/booking/guests", "post");
        var update = await StatusesFor("/api/booking/guests/{guestId}", "put");

        create.Should().Contain(409, "Guest is [ConcurrencyAware]");
        create.Should().NotContain(404, "a create has no row to fail to find");
        update.Should().Contain(404, "an update does");
        update.Should().Contain(409);
    }

    /// <summary>The status codes the OpenAPI document declares for one operation.</summary>
    private async Task<IReadOnlyCollection<int>> StatusesFor(string path, string method)
    {
        var response = await GetRawAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the document is the contract; a run that cannot produce it has nothing to assert against");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        document.RootElement.GetProperty("paths").TryGetProperty(path, out var operations)
            .Should().BeTrue("the document should describe {0}", path);

        operations.TryGetProperty(method, out var operation)
            .Should().BeTrue("{0} should accept {1}", path, method.ToUpperInvariant());

        return operation.GetProperty("responses").EnumerateObject()
            .Select(p => int.Parse(p.Name))
            .ToList();
    }
}
