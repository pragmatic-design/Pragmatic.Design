using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     What a host composed with <c>[RemoteBoundary&lt;T&gt;]</c> does with the module's
///     in-process workers.
/// </summary>
/// <remarks>
///     <para>
///         <b>The fact this suite exists for.</b> <c>AddRemote</c> registers the boundary's public
///         interface and nothing else — not <c>I{Boundary}InternalActions</c>, and not a group's
///         internal twin. That is correct and cannot be otherwise: the internal interface publishes
///         the preloaded shapes, which take a tracked entity, and a tracked entity does not cross a
///         process. Pinned by
///         <c>AnInternalOperationJoinsItsGroupTests.RemoteMode_RegistersNeitherInternalInterface</c>.
///     </para>
///     <para>
///         <b>What made it a defect rather than a documentation line.</b> The host kept registering
///         the remote module's message handlers, event handlers, jobs and sagas — code of that module,
///         which runs its operations and therefore reaches for its internal interface. Measured on
///         <c>Showcase.Host.Distributed</c>, which composes Billing as remote and still emitted
///         <c>Showcase.Billing.Generated.PragmaticMessageHandlerRegistration.AddPragmaticMessageHandlers</c>:
///         that registers <c>ReservationConfirmedHandler</c>, whose constructor takes
///         <c>IBillingInternalActions</c>. The container cannot build it, and the message is nacked and
///         dropped with no row and no error.
///     </para>
///     <para>
///         ⚠️ And it is not only a resolution failure. A handler of a module hosted elsewhere is
///         subscribed <b>twice</b> — here and in the host that owns the module — so the half that did
///         resolve would do the work a second time.
///     </para>
///     <para>
///         The control is <see cref="HostWiringFixture.Control" />: the same library, the same
///         references, hosted instead of reached. Without it "the remote host does not wire handlers"
///         is equally satisfied by a generator that stopped wiring handlers at all.
///     </para>
///     <para>
///         ⚠️ <b>Not asserted here: domain event handlers and sagas.</b> They take the same one-line
///         filter on the same metadata channel, but the probe library declares neither, so a test for
///         them would pass on an empty list and measure nothing. Said rather than written.
///     </para>
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class AModuleHostedElsewhereDoesNotRunHereTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";

    [Fact]
    public void TheRemoteModulesMessageHandlers_AreNotRegisteredHere()
        => WiredInTheRemoteHost(".AddPragmaticMessageHandlers(services);").Should().BeEmpty(
            "a [MessageHandler] of a module reached over HTTP runs in the process that owns the "
            + "module: registered here it cannot be constructed — its operations' internal interface "
            + "is not registered by AddRemote — and, where it can, it handles the message twice");

    [Fact]
    public void TheRemoteModulesJobs_AreNotRegisteredHere()
        => WiredInTheRemoteHost(".AddDiscoveredJobs(services);").Should().BeEmpty(
            "a [RecurringJob] of a module reached over HTTP is scheduled in the process that owns it; "
            + "here it would run the module's operations against a boundary that is an HTTP proxy");

    /// <summary>
    ///     The control, and the reason the three above mean anything: hosted rather than reached, the
    ///     very same declarations are wired.
    /// </summary>
    [Theory]
    [InlineData(".AddPragmaticMessageHandlers(services);")]
    [InlineData(".AddDiscoveredJobs(services);")]
    public void TheSameDeclarations_AreWiredWhenTheHostOwnsTheModule(string registration)
        => HostWiringFixture.LinesOf(fixture.Control, HostServices)
            .Where(line => line.Contains(registration, StringComparison.Ordinal))
            .Should().NotBeEmpty(
                $"the control host references the same library and hosts it, so it must emit {registration} — "
                + "an empty control means the probe stopped declaring these, and the absences above "
                + "would then be measuring nothing");

    /// <summary>
    ///     The topology is still applied: the boundary is added, in remote mode.
    /// </summary>
    /// <remarks>
    ///     Without this, "the remote host wires none of the module's workers" is also satisfied by a
    ///     host that failed to notice the module at all.
    /// </remarks>
    [Fact]
    public void TheBoundaryItself_IsStillAddedInRemoteMode()
        => HostWiringFixture.LinesOf(fixture.Remote, HostServices)
            .Where(line => line.Contains("BoundaryMode.Remote", StringComparison.Ordinal))
            .Should().NotBeEmpty("the remote composition is what the rest of this class is about");

    private IReadOnlyList<string> WiredInTheRemoteHost(string registration)
        => [.. HostWiringFixture.LinesOf(fixture.Remote, HostServices)
            .Where(line => line.Contains(registration, StringComparison.Ordinal))];
}
