// Pragmatic.Composition.HostWiring.Tests - Declaration, not presence
// Asserts on the generated host, because the question is what a consumer's application ends up
// registering, not what a detector returned.

using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A capability is wired because somebody declared it, not because its assembly is on the
///     compilation.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Every probe in <c>FeatureDetector</c> asks <c>TypeExists</c>, and an assembly is on the
///         compilation whether the application asked for it or somebody else's dependency brought it.
///         Measured on a consumer application: its host references <c>Pragmatic.Resilience</c>, uses it
///         in <b>zero</b> files, and removing the reference changes nothing — the package arrives
///         transitively and <c>Host.Services.g.cs</c> still contains
///         <c>services.AddPragmaticResilience();</c>.
///     </para>
///     <para>
///         ⚠️ Direct-versus-transitive cannot decide it: that host references the package directly
///         <b>and</b> receives it transitively, so no rule about who brought the assembly separates the
///         two. "Does anybody declare this" is a source question, and source is what a generator reads.
///     </para>
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class ACapabilityNobodyDeclaredIsNotWiredTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";

    private const string ResilienceRegistration = "services.AddPragmaticResilience();";

    /// <summary>
    ///     A host that references the package and declares nothing does not get the registration.
    /// </summary>
    [Fact]
    public void Resilience_ReferencedAndNeverDeclared_IsNotRegistered()
    {
        var lines = HostWiringFixture.LinesOf(fixture.Bare, HostServices);

        lines.Should().NotContain(ResilienceRegistration,
            "nothing in this application declares a resilience policy, and the package is on the "
            + $"compilation only because something brought it. Host.Services.g.cs has {lines.Count} lines");
    }

    /// <summary>
    ///     ⚠️ The control, and the one that keeps the change from being "turn the feature off".
    /// </summary>
    /// <remarks>
    ///     A declaration living in a <b>referenced module</b> still counts: the metadata is what crosses
    ///     the assembly boundary, and a rule that read only the host's own syntax would wire nothing in
    ///     every multi-module application — which is all of them.
    /// </remarks>
    [Fact]
    public void Resilience_DeclaredInAReferencedModule_IsRegistered()
    {
        var lines = HostWiringFixture.LinesOf(fixture.Control, HostServices);

        lines.Should().Contain(ResilienceRegistration,
            "the probe library declares a resilience policy, so the host it is referenced from has a "
            + $"reason to wire it. Host.Services.g.cs has {lines.Count} lines");
    }

    /// <summary>
    ///     And a declaration in the host's own source counts too.
    /// </summary>
    /// <remarks>
    ///     The host's own metadata is built by this generator run rather than read back from an
    ///     assembly attribute it cannot see, so this is the half that a naive "read the references"
    ///     implementation would get wrong — silently, in the dangerous direction.
    /// </remarks>
    [Fact]
    public void Resilience_DeclaredInTheHostItself_IsRegistered()
    {
        var lines = HostWiringFixture.LinesOf(fixture.Subject, HostServices);

        lines.Should().Contain(ResilienceRegistration,
            $"the host declares it in its own source. Host.Services.g.cs has {lines.Count} lines");
    }

    /// <summary>
    ///     The same, for every other capability that has a declaration to read.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A capability that is only referenced is not wired: a bare host that declares nothing gets
    ///     none of the capabilities gated on a declaration. The ones with nothing to declare are
    ///     asserted below.
    /// </remarks>
    [Theory]
    [InlineData("services.AddPragmaticCaching();", "[Cacheable] / [InvalidatesCache]")]
    [InlineData("services.AddPragmaticMessaging();", "[MessageHandler]")]
    [InlineData("services.AddPragmaticJobs();", "[Job] / [RecurringJob]")]
    [InlineData("services.AddPragmaticInternationalization(configuration);", "a translation file")]
    [InlineData("services.AddPragmaticFeatureFlags();", "an IFeatureFlag")]
    public void ACapabilityWithADeclarationSite_ReferencedAndNeverDeclared_IsNotRegistered(
        string registration, string declaration)
    {
        var lines = HostWiringFixture.LinesOf(fixture.Bare, HostServices);

        lines.Should().NotContain(registration,
            $"nothing here writes {declaration}, so nothing asked for this capability; "
            + $"Host.Services.g.cs has {lines.Count} lines");
    }

    /// <summary>
    ///     And the same ones, wired when the referenced module does declare them.
    /// </summary>
    /// <remarks>
    ///     The half that makes this a gate rather than a removal.
    /// </remarks>
    [Theory]
    [InlineData("services.AddPragmaticMessaging();")]
    [InlineData("services.AddPragmaticJobs();")]
    [InlineData("services.AddPragmaticInternationalization(configuration);")]
    [InlineData("services.AddPragmaticFeatureFlags();")]
    public void ACapabilityDeclaredInAReferencedModule_IsStillRegistered(string registration)
    {
        var lines = HostWiringFixture.LinesOf(fixture.Control, HostServices);

        lines.Should().Contain(registration,
            $"the probe library declares it; Host.Services.g.cs has {lines.Count} lines");
    }

    /// <summary>
    ///     ⚠️ The control that stops this from turning half the framework off.
    /// </summary>
    /// <remarks>
    ///     A capability with no declaration site of its own — the JSON seam has no attribute anybody
    ///     writes — keeps being wired from presence, because there is nothing to declare and presence
    ///     <em>is</em> the signal. Without this assertion, a change that silenced every capability
    ///     would look exactly like the rule working.
    /// </remarks>
    [Fact]
    public void ACapabilityWithNoDeclarationSite_IsStillWiredFromPresence()
    {
        var lines = HostWiringFixture.LinesOf(fixture.Bare, HostServices);

        lines.Should().Contain("services.AddPragmaticMultiTenancy();",
            "the tenant middleware has no attribute anybody writes, so its presence is the only signal "
            + $"there is; Host.Services.g.cs has {lines.Count} lines");
        lines.Should().Contain("services.AddPragmaticIdentity();",
            "and neither has the identity middleware");
        lines.Should().Contain("services.AddPragmaticTemporalAspNetCore();",
            "nor the temporal request context");
    }
}
