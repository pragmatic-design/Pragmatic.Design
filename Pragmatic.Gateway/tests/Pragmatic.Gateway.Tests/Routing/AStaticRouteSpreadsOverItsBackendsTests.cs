using Pragmatic.Gateway.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Gateway.Tests.Routing;

/// <summary>
///     A static route names every instance of the service it fronts, and the requests
///     through it reach all of them.
/// </summary>
/// <remarks>
///     Configured the way an operator configures it — <c>Gateway:Routes:0:Backends:0</c>, <c>:1</c> — and
///     asserted through the real gateway in front of two real listeners, because what a service that runs
///     twice needs is both instances answering, not a cluster object with two entries.
/// </remarks>
public sealed class AStaticRouteSpreadsOverItsBackendsTests : IAsyncLifetime
{
    private EchoBackend _first = null!;
    private EchoBackend _second = null!;

    public async Task InitializeAsync()
    {
        _first = await EchoBackend.StartAsync("first").ConfigureAwait(false);
        _second = await EchoBackend.StartAsync("second").ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await _first.DisposeAsync().ConfigureAwait(false);
        await _second.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task TwoBackends_BothAnswerThroughOneRoute()
    {
        using var gateway = new ConfiguredGatewayFactory(new Dictionary<string, string?>
        {
            ["Gateway:Routes:0:RouteId"] = "svc",
            ["Gateway:Routes:0:Path"] = "/svc/{**catch-all}",
            ["Gateway:Routes:0:Backends:0"] = _first.Address,
            ["Gateway:Routes:0:Backends:1"] = _second.Address,
        });
        using var client = gateway.CreateClient();

        var answeredBy = new List<string>();
        for (var i = 0; i < 8; i++)
            answeredBy.Add((await client.GetStringAsync("/svc/ping")).Split(' ')[0]);

        // ⚠️ The exact alternation, not "both were seen": random picks satisfy the second four times in five
        // over four requests, and that is how this test stayed green while the gateway balanced nothing.
        // Eight strictly alternating answers happen by chance once in 128.
        answeredBy.Distinct().Should().BeEquivalentTo(["first", "second"]);
        answeredBy.Zip(answeredBy.Skip(1)).Should().OnlyContain(pair => pair.First != pair.Second,
            $"the route takes its backends in turn, and was answered by {string.Join(", ", answeredBy)}");
    }

    /// <summary>The control: one backend is one destination, and it answers every request.</summary>
    [Fact]
    public async Task OneBackend_AnswersEveryRequest()
    {
        using var gateway = new ConfiguredGatewayFactory(new Dictionary<string, string?>
        {
            ["Gateway:Routes:0:RouteId"] = "svc",
            ["Gateway:Routes:0:Path"] = "/svc/{**catch-all}",
            ["Gateway:Routes:0:Backends:0"] = _first.Address,
        });
        using var client = gateway.CreateClient();

        var answers = new List<string>();
        for (var i = 0; i < 4; i++)
            answers.Add(await client.GetStringAsync("/svc/ping"));

        answers.Should().OnlyContain(answer => answer.StartsWith("first ", StringComparison.Ordinal),
            "there is nowhere else for the request to go");
    }

    /// <summary>A route with no backend is a configuration mistake, and the gateway says which route.</summary>
    [Fact]
    public void NoBackend_FailsTheStart_NamingTheRoute()
    {
        using var gateway = new ConfiguredGatewayFactory(new Dictionary<string, string?>
        {
            ["Gateway:Routes:0:RouteId"] = "orphan",
            ["Gateway:Routes:0:Path"] = "/orphan/{**catch-all}",
        });

        var start = () => gateway.CreateClient();

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*orphan*");
    }
}
