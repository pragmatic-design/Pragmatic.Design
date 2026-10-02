// Pragmatic.Composition.HostWiring.Tests - The middleware and the services it needs
// Asserts on the generated host: the two lines are emitted by different parts of the template, and
// only the generated output shows whether they agree.

using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     <c>UseRateLimiter</c> throws at startup when <c>AddRateLimiter</c> was not called, so
///     the host registers the step that calls it only where it also registers the services.
/// </summary>
/// <remarks>
///     A host whose modules expose no endpoint yet — every application before its first one — has the
///     Endpoints package on the compilation and no endpoint that asks for the services. Keyed on the
///     package, the step would be registered without the services, and the host would not start.
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class TheRateLimiterComesWithItsServicesTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";

    private const string RateLimiterStep =
        "services.AddSingleton<global::Pragmatic.Composition.Abstractions.IStartupStep, global::Pragmatic.Composition.Steps.RateLimiterStep>();";

    private const string RateLimiterServices = "services.AddRateLimiter(rateLimiterOptions =>";

    [Fact]
    public void AHostWithNoEndpoint_RegistersNeitherTheMiddlewareNorItsServices()
    {
        var lines = HostWiringFixture.LinesOf(fixture.Bare, HostServices);

        lines.Should().NotContain(RateLimiterServices,
            $"no endpoint exists, so nothing asks for a limiter; Host.Services.g.cs has {lines.Count} lines");
        lines.Should().NotContain(RateLimiterStep,
            "the step calls UseRateLimiter, which throws at startup when AddRateLimiter was not called");
    }

    /// <summary>
    ///     ⚠️ The control: a host with endpoints keeps both, or the limits they declare are metadata
    ///     nobody enforces.
    /// </summary>
    [Fact]
    public void AHostWithEndpoints_RegistersTheMiddlewareAndItsServices()
    {
        var lines = HostWiringFixture.LinesOf(fixture.Control, HostServices);

        lines.Should().Contain(RateLimiterServices,
            $"the probe library exposes endpoints; Host.Services.g.cs has {lines.Count} lines");
        lines.Should().Contain(RateLimiterStep,
            "and without the middleware every [RateLimit] permits every request");
    }
}
