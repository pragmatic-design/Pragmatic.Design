using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A compensator declared on an action the host reaches over HTTP (PRAG1689).
/// </summary>
/// <remarks>
///     <para>
///         <c>[UndoWith&lt;T&gt;]</c> is executed by the invoker that owns the action. Put that action
///         behind a <c>[RemoteBoundary&lt;T&gt;]</c> and the caller holds an HTTP proxy instead: the
///         attribute is still there, reads as protection, and nothing in this process will ever run it.
///     </para>
///     <para>
///         The module cannot see this — it compiles the same whether it is hosted or called. The host
///         is the only compilation that knows the topology, which is why the check lives here and why
///         the same declarations must stay silent when the module is local.
///     </para>
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class RemoteCompensationTests(HostWiringFixture fixture)
{
    [Fact]
    public void CompensationBehindARemoteBoundary_IsReported()
    {
        fixture.RemoteHostDiagnostics.Should().Contain("PRAG1689",
            "the undo runs in the process that owns the action, not in this one");
    }

    /// <summary>
    ///     The same module, hosted rather than called: the compensator runs, so there is nothing to say.
    /// </summary>
    [Fact]
    public void TheSameCompensation_HostedLocally_IsNotReported()
    {
        fixture.ControlDiagnostics.Should().NotContain("PRAG1689");
    }
}
