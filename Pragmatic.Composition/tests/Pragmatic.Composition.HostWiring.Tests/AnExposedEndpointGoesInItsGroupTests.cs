using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     <c>[ExposeEndpoint&lt;TAction, TGroup&gt;]</c> is mapped inside <c>TGroup</c>.
/// </summary>
/// <remarks>
///     <para>
///         The second type argument was read into the model and written into the module metadata, and
///         nothing downstream read it: the route went on the host root (or the package prefix), outside
///         the group's path and outside every option <c>ConfigureGroup</c> puts on it.
///     </para>
///     <para>
///         ⚠️ The probe's group is used by no <c>[Endpoint]</c>, so the library's endpoint metadata does
///         not list it. That is the case that matters: an application exposing a package action under a
///         group of its own has no other endpoint in that group to carry it into the metadata.
///     </para>
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class AnExposedEndpointGoesInItsGroupTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";

    [Fact]
    public void TheGroupNamedOnlyByAnExposedEndpoint_IsMappedByTheHost()
        => HostWiringFixture.LinesOf(fixture.AuxHosted, HostServices)
            .Where(line => line.Contains("probeGroupedGroup = root.MapGroup(\"/probe-grouped\")", StringComparison.Ordinal))
            .Should().NotBeEmpty(
                "the exposed endpoint names ProbeGroupedGroup, so the host builds that group's MapGroup "
                + "even though no [Endpoint] in the library uses it");

    [Fact]
    public void AnExposedEndpointWithAGroup_IsMappedInsideIt()
        => HostWiringFixture.LinesOf(fixture.AuxHosted, HostServices)
            .Where(line => line.Contains("ProbeAuxGroupedActionEndpointHandler.MapEndpoint(probeGroupedGroup)", StringComparison.Ordinal))
            .Should().NotBeEmpty(
                "[ExposeEndpoint<ProbeAuxGroupedAction, ProbeGroupedGroup>] publishes the route under "
                + "/probe-grouped, with the group's options, not on the root");

    /// <summary>The control: the same attribute without a group keeps today's route.</summary>
    [Fact]
    public void AnExposedEndpointWithoutAGroup_StaysWhereItWas()
        => HostWiringFixture.LinesOf(fixture.AuxHosted, HostServices)
            .Where(line => line.Contains("ProbeAuxActionEndpointHandler.MapEndpoint(root)", StringComparison.Ordinal))
            .Should().NotBeEmpty("no group was named, so nothing about its mapping changes");
}
