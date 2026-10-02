using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host that reaches a module over HTTP keeps the container validation it was given.
/// </summary>
/// <remarks>
///     <para>
///         The generated entry point of a host with a <c>[RemoteBoundary&lt;T&gt;]</c> does not emit
///         <c>builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false)</c>.
///     </para>
///     <para>
///         ⚠️ <b>Why it must not.</b> <c>ValidateOnBuild</c> is the only thing that walks every registered
///         descriptor and says, at startup, that one of them cannot be constructed. Off for the whole
///         process, it hides every unresolvable registration in that host, not the one it was turned off
///         for — for example a message handler of the remote module whose constructor takes that
///         module's internal boundary interface, which <c>AddRemote</c> does not register.
///     </para>
///     <para>
///         A host does not register the in-process workers — message handlers, event handlers, jobs,
///         sagas — of a module it reaches over HTTP, so there are no cross-boundary handlers in the
///         container to excuse the line. Without it the host keeps ASP.NET Core's own default:
///         validation on in Development, which is where a developer is told.
///     </para>
///     <para>
///         A host whose <c>[Include]</c>s do not cover every domain module it references is the other
///         half of the same question, asserted in <see cref="AStandaloneHostWiresOnlyWhatItIncludesTests" />.
///     </para>
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class ARemoteHostStillValidatesItsContainerTests(HostWiringFixture fixture)
{
    private const string HostEntry = "Host.Entry.g.cs";

    [Fact]
    public void TheRemoteHostsEntryPoint_DoesNotTurnOffContainerValidation()
        => HostWiringFixture.LinesOf(fixture.Remote, HostEntry)
            .Where(line => line.Contains("ValidateOnBuild", StringComparison.Ordinal))
            .Should().BeEmpty(
                "a host that reaches a module over HTTP no longer registers that module's in-process "
                + "workers, so there is nothing left in its container that it knew could not "
                + "be built — and switching the validator off hides every other unresolvable "
                + "registration with it");

    /// <summary>
    ///     The control: the entry point is still generated, and still builds an application.
    /// </summary>
    /// <remarks>
    ///     Without it, "no ValidateOnBuild line" is equally satisfied by a host whose entry point was
    ///     not generated at all — which is what an empty dictionary looks like to the assertion above.
    /// </remarks>
    [Fact]
    public void TheRemoteHostsEntryPoint_IsStillGenerated()
        => HostWiringFixture.LinesOf(fixture.Remote, HostEntry)
            .Where(line => line.Contains("builder.Build();", StringComparison.Ordinal))
            .Should().NotBeEmpty("the remote host is still a host");

    /// <summary>And the host that owns the module never had the line to begin with.</summary>
    [Fact]
    public void TheHostThatOwnsTheModule_ValidatesAsItAlwaysDid()
        => HostWiringFixture.LinesOf(fixture.Control, HostEntry)
            .Where(line => line.Contains("ValidateOnBuild", StringComparison.Ordinal))
            .Should().BeEmpty("a host that hosts everything it references was never the case in question");
}
