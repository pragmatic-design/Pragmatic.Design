using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host that includes one of the two modules it references, and keeps its container validation.
/// </summary>
/// <remarks>
///     <para>
///         Such a host could seem to need
///         <c>UseDefaultServiceProvider(o => o.ValidateOnBuild = false)</c>, on the ground that it
///         registers the workers of modules it does not include — whose boundaries are never
///         <c>AddLocal</c>'d, so the internal interfaces those workers inject would be absent and the
///         container could not build them.
///     </para>
///     <para>
///         It does not register them. <c>HostModeGenerator.BuildIncludedAssemblyNames</c> filters
///         <c>assemblyMetadata</c> — and with it every worker registration — down to the assemblies the
///         host includes. <c>Showcase.Billing.Host</c> references Booking, Catalog and Accounts, and its
///         generated services name none of them.
///     </para>
///     <para>
///         ⚠️ The one exception in that filter is <c>[RemoteBoundary&lt;T&gt;]</c>, whose assemblies the
///         set deliberately adds (their actions are needed for the HTTP invokers). Those assemblies
///         contribute no worker registrations. With both of those true, nothing is left that the host
///         knows it cannot build, so validation stays on.
///     </para>
///     <para>
///         The cases below pin the filter as well as the entry point: a filter nobody asserts is one
///         removal away from putting the hole back.
///     </para>
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class AStandaloneHostWiresOnlyWhatItIncludesTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";
    private const string HostEntry = "Host.Entry.g.cs";

    [Fact]
    public void ItDoesNotTurnOffContainerValidation()
        => HostWiringFixture.LinesOf(fixture.Standalone, HostEntry)
            .Where(line => line.Contains("ValidateOnBuild", StringComparison.Ordinal))
            .Should().BeEmpty(
                "the reason given for switching it off — that this host registers workers it cannot "
                + "build — is not true: they are filtered out by assembly. Off for the process, it "
                + "hides every other unresolvable registration with them");

    /// <summary>
    ///     The measured fact the removal rests on, made permanent.
    /// </summary>
    [Fact]
    public void ItDoesNotWireTheModuleItLeavesOut()
        => HostWiringFixture.LinesOf(fixture.Standalone, HostServices)
            .Where(line => line.Contains("Probe.Aux", StringComparison.Ordinal))
            .Should().BeEmpty(
                "a module this host references and does not include is not hosted here, so neither its "
                + "message handler nor its recurring job belongs in this container");

    /// <summary>
    ///     The control, and what makes the two above mean anything: the module it <b>does</b> include
    ///     is wired.
    /// </summary>
    /// <remarks>
    ///     Without it, "wires nothing of the module it leaves out" is equally satisfied by a host that
    ///     failed to wire anything at all — which is what an unparsed or ungenerated file looks like.
    /// </remarks>
    [Theory]
    [InlineData(".AddPragmaticMessageHandlers(services);")]
    [InlineData(".AddDiscoveredJobs(services);")]
    public void ItStillWiresTheModuleItIncludes(string registration)
        => HostWiringFixture.LinesOf(fixture.Standalone, HostServices)
            .Where(line => line.Contains(registration, StringComparison.Ordinal))
            .Should().NotBeEmpty($"the included module declares both, so the host must emit {registration}");

    /// <summary>
    ///     And not its boundary either, although that module imports a package.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ A boundary extension registers the invokers of the packages its module imports, so the
    ///         loop that emits <c>Add{Module}Boundary</c> collects importers through the same
    ///         <c>[Include&lt;T&gt;]</c> filter the <c>Discovered*</c> lists go through. Collected from
    ///         <b>every</b> module in the compilation, a host would call the boundary extension of a
    ///         module it does not host, registering that package's invokers without its stores, and
    ///         <c>builder.Build()</c> would throw before any request — "Unable to resolve service for
    ///         type 'IRolePermissionStore'".
    ///     </para>
    ///     <para>
    ///         ⚠️ Removing the filter turns two cases red: this one and
    ///         <see cref="ItDoesNotWireTheModuleItLeavesOut" />. The second can catch it only because
    ///         this suite's left-out module imports a package; with a probe that imports none, the branch
    ///         is never taken. Such a gap is in the <b>probe</b>, not in the assertion.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ItDoesNotWireTheBoundaryOfTheModuleItLeavesOut()
        => HostWiringFixture.LinesOf(fixture.Standalone, HostServices)
            .Where(line => line.Contains("AddProbeAuxBoundary", StringComparison.Ordinal))
            .Should().BeEmpty(
                "a boundary extension registers its module's package invokers, and this host registers "
                + "none of that package's stores because it does not host the module that imports it");

    /// <summary>
    ///     The control: a host that includes nothing hosts everything, and does call it.
    /// </summary>
    /// <remarks>
    ///     Without it, "the left-out module's boundary is not wired" is satisfied by never wiring that
    ///     boundary anywhere — which would break every monolith instead.
    /// </remarks>
    [Fact]
    public void AHostThatIncludesNothing_StillWiresThatBoundary()
        => HostWiringFixture.LinesOf(fixture.AuxHosted, HostServices)
            .Where(line => line.Contains("AddProbeAuxBoundary", StringComparison.Ordinal))
            .Should().NotBeEmpty(
                "declaring no topology means hosting everything referenced, and that module's package "
                + "invokers are then this host's to register");

    /// <summary>The package has to have compiled, or the two cases above pin nothing.</summary>
    [Fact]
    public void ThePackageTheLeftOutModuleImports_Compiles()
        => fixture.PackageLibraryEmitErrors.Should().BeEmpty();

    /// <summary>The second library has to have compiled, or the shape above is not the shape.</summary>
    [Fact]
    public void TheModuleItLeavesOut_WasBuiltAndReferenced()
    {
        fixture.AuxLibraryEmitErrors.Should().BeEmpty(
            "an aux library that did not emit is not referenced, and then 'wires nothing of it' is "
            + "true for a reason that has nothing to do with the topology");

        HostWiringFixture.LinesOf(fixture.Control, "Host.Services.g.cs")
            .Should().NotBeEmpty("the control host is still generated");
    }
}
