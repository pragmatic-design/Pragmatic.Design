using Pragmatic.Composition.ControlPlane;
using Pragmatic.Composition.Metadata;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.Tests.ControlPlane;

/// <summary>
///     A host's logical name is its own, not the one of whichever assembly loaded first.
/// </summary>
/// <remarks>
///     <para>
///         The generator emits one <c>HostTopology</c> metadata provider per host, registered from a
///         <c>[ModuleInitializer]</c>, and <c>AssemblyMetadataRegistry.FindByCategory</c> returns the
///         <b>first</b> match. So two hosts in one process both read the first provider's topology, and
///         the second host's control-plane identity — what <c>IHostIdentity.HostName</c> labels its
///         health and everything keyed on it with — is filed under its neighbour's name.
///     </para>
///     <para>
///         ⚠️ First-writer-wins here, where the OpenAPI document was last-writer-wins and the
///         manifest was shared. Three registries, three different answers to "which host is
///         this", and all three came from the same shape: a fact about one host kept in a static of the
///         process.
///     </para>
///     <para>
///         ⚠️ <b>The registry cannot be cleared</b> — it is append-only by design, which is right for a
///         registry filled by module initializers and awkward for a test. So nothing here asserts
///         <em>which</em> name the registry answers with: what is asserted is that two identities built
///         the way the generated host builds them are <b>different</b>, which is the property that was
///         false, and that one built without a name still answers from the registry, which is the
///         fallback that keeps every existing host working.
///     </para>
/// </remarks>
[Collection(TheProcessWideMetadataRegistryCollection.Name)]
public sealed class TheHostKnowsItsOwnNameTests
{
    /// <summary>Two hosts in one process, as their generated compositions build them.</summary>
    [Fact]
    public void TwoHostsInOneProcess_EachReportsItsOwnName()
    {
        AssemblyMetadataRegistry.Register(new TopologyOf("Intake"));
        AssemblyMetadataRegistry.Register(new TopologyOf("Verify"));

        var intake = new LocalHostIdentity(hostName: "Intake");
        var verify = new LocalHostIdentity(hostName: "Verify");

        intake.HostName.Should().Be("Intake");
        verify.HostName.Should().Be("Verify",
            "the second host's name is its own, whichever assembly the runtime loaded first");
    }

    /// <summary>
    ///     The control that keeps every host that has not been regenerated working: with no name of its
    ///     own, the identity still reads the registry.
    /// </summary>
    /// <remarks>
    ///     A host generated without a name of its own passes nothing, and the registry is where its name
    ///     comes from. Breaking that would trade a wrong name for no name, silently.
    /// </remarks>
    [Fact]
    public void AHostWithNoNameOfItsOwn_StillReadsTheRegistry()
    {
        AssemblyMetadataRegistry.Register(new TopologyOf("FromTheRegistry"));

        var fromRegistry = AssemblyMetadataRegistry.FindByCategory(MetadataCategory.HostTopology);
        fromRegistry.Should().NotBeNull("the registry has at least the provider this test added");

        new LocalHostIdentity().HostName.Should().Be(NameIn(fromRegistry!.Value.JsonData),
            "which is the first provider's topology, and that is exactly the defect this issue records");
    }

    /// <summary>
    ///     And an empty name is not a name: it falls back rather than labelling the host with nothing.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without this, "the host passes its own name" would be satisfied by a generated host that
    ///     passes an empty string — which is what a root namespace nobody set would produce — and the
    ///     control plane would label it "".
    /// </remarks>
    [Fact]
    public void AnEmptyNameFallsBack()
    {
        AssemblyMetadataRegistry.Register(new TopologyOf("NotEmpty"));

        new LocalHostIdentity(hostName: "  ").HostName.Should().NotBe("  ");
        new LocalHostIdentity(hostName: "").HostName.Should().NotBeEmpty();
    }

    private static string NameIn(string topologyJson)
        => System.Text.Json.JsonDocument.Parse(topologyJson).RootElement.GetProperty("host").GetString()!;

    /// <summary>One host's topology, as the generator writes it.</summary>
    private sealed class TopologyOf(string host) : IAssemblyMetadataProvider
    {
        public IReadOnlyList<AssemblyMetadataEntry> GetMetadata() =>
        [
            new(MetadataCategory.HostTopology, "1.0", $"{{\"host\":\"{host}\",\"includes\":[]}}")
        ];
    }
}
