using System.Net;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Testing.Tests;

/// <summary>
///     "Authorized" is a claim about a difference, and a difference needs two responses.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <see cref="PragmaticHttpAssertions.ShouldNotBeForbidden" /> passes on anything that is not
///         401 or 403 — including a 400 from a middleware that refused the request before it reached a
///         route. Paired with <see cref="PragmaticHttpAssertions.ShouldBeRejected" />, which accepts any
///         4xx, that made a blanket 400 satisfy <b>both</b> halves of the authorization contract:
///         fifty-four generated tests reported success while every request was being refused.
///     </para>
///     <para>
///         The invariant that closes it does not need to know why: if the endpoint answered the
///         privileged and the unprivileged caller the same way, the permission changed nothing that can
///         be observed.
///     </para>
/// </remarks>
public class ThePairIsTheMeasurementTests
{
    /// <summary>The mask, asked directly: both callers refused before routing.</summary>
    [Fact]
    public void TwoIdenticalStatuses_Fail()
    {
        var act = () => Response(HttpStatusCode.BadRequest)
            .ShouldBeAuthorizedUnlike(Response(HttpStatusCode.BadRequest));

        act.Should().Throw<PragmaticTestAssertionException>();
    }

    /// <summary>
    ///     The control: the outcomes a working pair produces are all different, and all pass.
    /// </summary>
    /// <remarks>
    ///     Without it, "identical statuses fail" would also be satisfied by an assertion that fails on
    ///     everything — and the three pairs below are what the contract actually looks like against a
    ///     real application: a read of a random id, a create the shape cannot carry, a create it can.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest, HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Created, HttpStatusCode.Forbidden)]
    public void DifferentStatuses_Pass(HttpStatusCode authorized, HttpStatusCode denied)
    {
        Response(authorized).ShouldBeAuthorizedUnlike(Response(denied));
    }

    /// <summary>And the old claim still holds: a forbidden privileged caller fails.</summary>
    /// <remarks>
    ///     The pair check is an addition, not a replacement: 403 against 200 differs, so a rule that
    ///     only compared the two would let the privileged caller be forbidden.
    /// </remarks>
    [Fact]
    public void AForbiddenPrivilegedCaller_StillFails()
    {
        var act = () => Response(HttpStatusCode.Forbidden)
            .ShouldBeAuthorizedUnlike(Response(HttpStatusCode.OK));

        act.Should().Throw<PragmaticTestAssertionException>();
    }

    /// <summary>The failure says what to look for, not just that it failed.</summary>
    /// <remarks>
    ///     The reader of this failure has to go looking for a middleware, and nothing in the two
    ///     statuses says so. A message that only reported "400 == 400" would send them to the endpoint.
    /// </remarks>
    [Fact]
    public void TheFailure_NamesWhatToLookFor()
    {
        var act = () => Response(HttpStatusCode.BadRequest)
            .ShouldBeAuthorizedUnlike(Response(HttpStatusCode.BadRequest));

        act.Should().Throw<PragmaticTestAssertionException>()
            .WithMessage("*before authorization runs*");
    }

    private static HttpResponseMessage Response(HttpStatusCode status)
        => new(status) { RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://app/api/probe") };
}
