using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     <c>[ExposeEndpoint&lt;T&gt;]</c> of a module the host does not host.
/// </summary>
/// <remarks>
///     <para>
///         A host does not map the routes of a module it reaches over HTTP, by filtering
///         <c>discoveredEndpoints</c>. <c>[ExposeEndpoint&lt;T&gt;]</c> does not travel on that list: it
///         is collected by <c>HostModeGenerator.CollectExposedEndpoints</c> <b>before</b> the
///         <c>BuildIncludedAssemblyNames</c> filter, and knowing nothing about
///         <c>[RemoteBoundary&lt;T&gt;]</c>.
///     </para>
///     <para>
///         ⚠️ The handlers are generated into the <b>host's own</b> namespace
///         (<c>{RootNamespace}.Endpoints.{Action}EndpointHandler</c>), so grepping a generated host for
///         the declaring module's name finds nothing. That is how the first reading of this missed them.
///     </para>
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class AnExposedEndpointFollowsItsModuleTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";

    /// <summary>The handler the aux module's <c>[ExposeEndpoint]</c> produces, whoever hosts it.</summary>
    private const string AuxHandler = "ProbeAuxActionEndpointHandler";

    [Fact]
    public void AModuleTheHostLeavesOut_HasNoExposedEndpointMappedHere()
        => HostWiringFixture.LinesOf(fixture.Standalone, HostServices)
            .Where(line => line.Contains(AuxHandler, StringComparison.Ordinal))
            .Should().BeEmpty(
                "this host includes the other module and not this one, so the action behind the "
                + "exposed endpoint has no invoker registered here and the route would answer 500");

    /// <summary>
    ///     The control, and the thing that makes the assertion above mean anything: a host that
    ///     <b>does</b> host the module maps it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It also answers a question the attribute's own documentation raises — it says the action
    ///     must be one imported with <c>[UsePackage&lt;T&gt;]</c>. If the generator enforced that, the
    ///     probe's own action would produce no handler at all and the absence above would be true for a
    ///     reason that has nothing to do with the topology.
    /// </remarks>
    [Fact]
    public void AHostThatOwnsTheModule_MapsItsExposedEndpoint()
        => HostWiringFixture.LinesOf(fixture.AuxHosted, HostServices)
            .Where(line => line.Contains(AuxHandler, StringComparison.Ordinal))
            .Should().NotBeEmpty(
                "the aux module declares [ExposeEndpoint<ProbeAuxAction>], and a host that includes it "
                + "serves that route");
}
