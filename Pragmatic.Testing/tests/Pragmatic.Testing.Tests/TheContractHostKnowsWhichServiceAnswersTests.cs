using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Testing.Tests;

/// <summary>
///     An application whose services are separate processes gives each boundary its own
///     client, instead of routing the generated requests itself.
/// </summary>
/// <remarks>
///     <para>
///         The generator already emits the contract tests <b>per boundary</b> —
///         <c>IntakeAuthContractTests</c>, <c>VerifyAuthContractTests</c> — so the boundary is in the
///         class's own name. What was missing was a way for the application to say which host each of
///         them talks to, and the only thing a request carries that hints at it is its <b>path</b>: a
///         prefix table somebody has to keep in step with the routes.
///     </para>
///     <para>
///         ⚠️ What that costs is not a clean failure. A route added to the second service under a prefix
///         the table does not name goes to the first one, which answers <b>404</b> — and a 404 is a
///         perfectly ordinary contract outcome, so the suite reports a failure about the wrong service.
///     </para>
/// </remarks>
[Collection(TheProcessWideContractHostCollection.Name)]
public sealed class TheContractHostKnowsWhichServiceAnswersTests : IDisposable
{
    public TheContractHostKnowsWhichServiceAnswersTests() => PragmaticContractHost.Reset();

    public void Dispose()
    {
        PragmaticContractHost.Reset();
        GC.SuppressFinalize(this);
    }

    /// <summary>A boundary's tests go to the client the application registered for it.</summary>
    [Fact]
    public void EachBoundaryGetsTheClientItWasRegisteredWith()
    {
        using var intake = new HttpClient { BaseAddress = new Uri("https://intake.test/") };
        using var verify = new HttpClient { BaseAddress = new Uri("https://verify.test/") };

        PragmaticContractHost.UseClientFor("Intake", intake);
        PragmaticContractHost.UseClientFor("Verify", verify);

        PragmaticContractHost.ClientFor("Intake").Should().BeSameAs(intake);
        PragmaticContractHost.ClientFor("Verify").Should().BeSameAs(verify);
    }

    /// <summary>
    ///     The control that keeps an application of one service unchanged: with no per-boundary client,
    ///     every boundary gets <c>Client</c>.
    /// </summary>
    /// <remarks>
    ///     Three of the four applications in this repository are one host, and none of them should have
    ///     to learn a second way to say so.
    /// </remarks>
    [Fact]
    public void WithOneServiceAndNoRegistration_EveryBoundaryGetsTheOneClient()
    {
        using var app = new HttpClient { BaseAddress = new Uri("https://app.test/") };
        PragmaticContractHost.Client = app;

        PragmaticContractHost.ClientFor("Booking").Should().BeSameAs(app);
        PragmaticContractHost.ClientFor("").Should().BeSameAs(app);
    }

    /// <summary>
    ///     The control that makes the seam worth having: once some boundary has a client of its own, one
    ///     that has none is refused by name — not answered from another service's host.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the whole point. Falling back to <c>Client</c> here would reintroduce exactly what
    ///     the route-prefix table did: the second service's contracts sent to the first one, answered
    ///     404, and reported as a contract failure about a service that never saw the request. The
    ///     message names the boundary that is missing and the ones that are registered, because "no
    ///     client" without which one is a second search.
    /// </remarks>
    [Fact]
    public void ABoundaryNobodyRegistered_IsRefusedByNameRatherThanSentSomewhereElse()
    {
        using var intake = new HttpClient { BaseAddress = new Uri("https://intake.test/") };
        using var single = new HttpClient { BaseAddress = new Uri("https://app.test/") };

        PragmaticContractHost.Client = single;
        PragmaticContractHost.UseClientFor("Intake", intake);

        var thrown = Record.Exception(() => PragmaticContractHost.ClientFor("Verify"));

        thrown.Should().BeOfType<InvalidOperationException>();
        thrown!.Message.Should().Contain("Verify", "the fixture is told which boundary it did not register")
            .And.Contain("Intake", "and which ones it did")
            .And.Contain(nameof(PragmaticContractHost.UseClientFor), "and what to call");
    }

    /// <summary>The application is told which boundary's contract it is preparing a request for.</summary>
    /// <remarks>
    ///     The second half of the same seam, and the reason the boundary is not enough on the client
    ///     alone: two services sign their tokens with different keys and map different roles, so the hook
    ///     that writes the caller has to know whose service is about to answer. Casework decided it by
    ///     matching <c>api/verifications</c> in the path.
    /// </remarks>
    [Fact]
    public void TheApplicationIsToldWhichBoundaryTheRequestBelongsTo()
    {
        var seen = new List<string>();
        PragmaticContractHost.PrepareRequest = contract =>
        {
            seen.Add(contract.Boundary);
            contract.Message.Headers.Add("X-Boundary", contract.Boundary);
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://app/api/verifications/1");

        PragmaticContractHost.Prepare(request, "GetVerification", "Verify")
            .Headers.GetValues("X-Boundary").Should().BeEquivalentTo(["Verify"]);
        seen.Should().BeEquivalentTo(["Verify"]);
    }

    /// <summary>A fixture that resets leaves nothing of its per-boundary clients behind.</summary>
    /// <remarks>
    ///     These are static, shared by every generated class of the run: a client the previous fixture
    ///     registered and this one did not would answer from a host that has been disposed.
    /// </remarks>
    [Fact]
    public void Reset_ForgetsThePerBoundaryClients()
    {
        using var intake = new HttpClient { BaseAddress = new Uri("https://intake.test/") };
        using var app = new HttpClient { BaseAddress = new Uri("https://app.test/") };

        PragmaticContractHost.UseClientFor("Intake", intake);
        PragmaticContractHost.Reset();
        PragmaticContractHost.Client = app;

        PragmaticContractHost.ClientFor("Intake").Should().BeSameAs(app,
            "nothing of the previous fixture's registration survived");
    }
}
