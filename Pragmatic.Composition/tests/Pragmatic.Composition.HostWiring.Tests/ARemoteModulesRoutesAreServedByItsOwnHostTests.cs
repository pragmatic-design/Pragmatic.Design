using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host that reaches a module over HTTP does not publish that module's routes.
/// </summary>
/// <remarks>
///     <para>
///         <c>[RemoteBoundary&lt;T&gt;]</c> is a <b>client</b> feature, not a gateway one. The routes of
///         a module belong to the host that owns it; a host that reaches it over HTTP reaches it
///         <b>from code</b>, through <c>I{Boundary}Actions</c>, and that is the whole of what
///         <c>AddRemote</c> gives it. One address in front of two services is a reverse proxy — a
///         deployment concern, not something the framework synthesises.
///     </para>
///     <para>
///         <b>Why mapping them would break.</b> The calling host does <b>not</b> register the invokers
///         those routes resolve — <c>PragmaticHostTemplate.DomainActions</c> excludes remote assemblies
///         from both action and mutation invokers, on purpose. A generated endpoint injects the invoker
///         for its one operation — the shape <c>PRAG0441</c> exists to enforce — so a remote module's
///         route mapped here would answer HTTP 500 at request time.
///     </para>
///     <para>
///         ⚠️ <b>The document follows the routes.</b> The aggregated manifest is built from the same
///         discovered metadata, so a host that does not serve those routes but still publishes them in
///         its OpenAPI document would have the same defect one layer up: a contract it does not serve.
///         Both halves are asserted below, because getting only the first right is the more likely
///         mistake.
///     </para>
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class ARemoteModulesRoutesAreServedByItsOwnHostTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";
    private const string OpenApiDocument = "_Infra.OpenApi.Generated.g.cs";

    /// <summary>A route only the probe library declares, so finding it means finding its module.</summary>
    private const string AProbeRoute = "api/probes";

    [Fact]
    public void TheRemoteModulesRoutes_AreNotMappedHere()
        => HostWiringFixture.LinesOf(fixture.Remote, HostServices)
            .Where(line => line.Contains(".MapEndpoint(", StringComparison.Ordinal)
                           && line.Contains("Probe.Domain", StringComparison.Ordinal))
            .Should().BeEmpty(
                "the module is hosted elsewhere and its invokers are deliberately not registered here, "
                + "so every one of these routes answers 500; they are served by the host that owns it");

    [Fact]
    public void TheRemoteModulesPaths_AreNotInThisHostsOpenApiDocument()
        => HostWiringFixture.LinesOf(fixture.Remote, OpenApiDocument)
            .Where(line => line.Contains(AProbeRoute, StringComparison.Ordinal))
            .Should().BeEmpty(
                "a host that does not serve a route must not publish it either — a contract it cannot "
                + "answer is the same defect one layer up");

    /// <summary>
    ///     The control for both: hosted rather than reached, the same declarations are mapped and
    ///     published.
    /// </summary>
    /// <remarks>
    ///     Without it, each assertion above is equally satisfied by a generator that stopped emitting
    ///     route mapping or stopped writing an OpenAPI document at all — and an ungenerated file is
    ///     exactly what an empty line list looks like.
    /// </remarks>
    [Fact]
    public void TheHostThatOwnsTheModule_MapsItsRoutes()
        => HostWiringFixture.LinesOf(fixture.Control, HostServices)
            .Where(line => line.Contains(".MapEndpoint(", StringComparison.Ordinal)
                           && line.Contains("Probe.Domain", StringComparison.Ordinal))
            .Should().NotBeEmpty("the control host hosts the module, so it serves its routes");

    /// <inheritdoc cref="TheHostThatOwnsTheModule_MapsItsRoutes" />
    [Fact]
    public void TheHostThatOwnsTheModule_PublishesItsPaths()
        => HostWiringFixture.LinesOf(fixture.Control, OpenApiDocument)
            .Where(line => line.Contains(AProbeRoute, StringComparison.Ordinal))
            .Should().NotBeEmpty(
                "the control host publishes what it serves — and an empty document here would mean the "
                + "absence asserted for the remote host proves nothing");
}
