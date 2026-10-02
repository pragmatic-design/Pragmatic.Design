using System.Net;
using Pragmatic.Testing;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     Two requests as the same person, carrying different permissions, get different answers.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A permission cache keyed by tenant and user id alone is right while the answer is resolved
///         from a store — and wrong the moment the application trusts the permission claims, because then
///         the answer is a function of the <b>token</b>. The first request would decide what that id can
///         do for as long as the entry lives.
///     </para>
///     <para>
///         The generated contract suite depends on it: the auth contracts call every endpoint as
///         <c>contract-{permission}</c> carrying that one permission, and a later request as the same id
///         with two of them must not be refused on the strength of the first.
///     </para>
///     <para>
///         The order is the one that fails: refused first, allowed second. The other way round the
///         defect reads as a caller being let in on an authority it no longer carries.
///     </para>
/// </remarks>
public class TheAuthorityIsTheTokensNotTheIdsTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Route = "/api/reservations/search?pageSize=1";

    [Fact]
    public async Task TheSameId_WithAnotherPermission_IsAnsweredForTheTokenItCarries()
    {
        var id = $"shared-{Guid.NewGuid():N}"[..16];

        var refused = await AnswerFor(id, "catalog.amenity.read");
        refused.Should().Be(HttpStatusCode.Forbidden,
            "the token carries a permission this route does not ask for");

        var allowed = await AnswerFor(id, "booking.reservation.read");
        allowed.Should().Be(HttpStatusCode.OK,
            "the same person, a token that carries the permission — and the earlier answer is not this one");
    }

    /// <summary>
    ///     The control: the same token twice is the same answer.
    /// </summary>
    /// <remarks>
    ///     Without it, "the answer follows the token" is satisfied by resolving the permissions on
    ///     every request and never caching — which is the cost the entry exists to avoid, and which no
    ///     assertion here would notice.
    /// </remarks>
    [Fact]
    public async Task TheSameToken_Twice_IsTheSameAnswer()
    {
        var id = $"steady-{Guid.NewGuid():N}"[..16];

        (await AnswerFor(id, "booking.reservation.read")).Should().Be(HttpStatusCode.OK);
        (await AnswerFor(id, "booking.reservation.read")).Should().Be(HttpStatusCode.OK);
    }

    /// <summary>One request as <paramref name="id"/>, carrying exactly <paramref name="permission"/>.</summary>
    private async Task<HttpStatusCode> AnswerFor(string id, string permission)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Route);
        PragmaticTestIdentity.AsUser(request, id, permissions: [permission]);

        using var response = await Client.SendAsync(request);
        return response.StatusCode;
    }
}
