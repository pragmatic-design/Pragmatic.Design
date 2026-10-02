using System.Net;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     An operation that is a <b>member</b> of a boundary can inject that boundary's internal facade and
///     invoke a sibling through it.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The failure mode is not a compile error: it <b>hangs the container</b>, without an error. A
///         facade that took one invoker per operation in its constructor would include the calling
///         operation's; building the facade would build that invoker, whose constructor asks for the facade
///         again. Microsoft's container detects cycles by walking constructor <b>parameters</b>: with the
///         invoker resolving the facade inside its own body, the cycle is outside that walk and the request
///         simply stops answering.
///     </para>
///     <para>
///         The shape <em>across</em> the boundary is covered by
///         <see cref="TheBoundaryInterfaceKeepsThePermission" /> and cannot have the cycle: another module's
///         facade does not contain the caller's invoker. The shape inside the same module is the one this
///         case measures.
///     </para>
/// </remarks>
public class TheFacadeOfItsOwnBoundary(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<Guid> AnOrderAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>The route answers — which is exactly what a cycle would prevent.</summary>
    [Fact]
    public async Task AnOperationInvokingASibling_ThroughItsOwnBoundary_Answers()
    {
        var response = await PostAsync("/api/orders/inside-boundary", new { orderId = await AnOrderAsync() });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the sibling operation succeeds and has nothing to return; with a cycle the request would "
            + "never come back, because building the boundary's own facade would close it");
    }

    /// <summary>
    ///     The control: the call really goes through the facade, instead of answering empty-handed.
    /// </summary>
    /// <remarks>
    ///     Without it, «the route answers 204» is also satisfied by an action that invokes nothing:
    ///     <c>VoidDomainAction</c> answers 204 by returning <c>Success</c>. An order that does not exist
    ///     makes the <em>invoked</em> operation fail, and that failure can reach here only through the
    ///     facade.
    /// </remarks>
    [Fact]
    public async Task AnIdThatNamesNoOrder_FailsWithTheSiblingsOwnRefusal()
    {
        var response = await PostAsync("/api/orders/inside-boundary", new { orderId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the refusal is the invoked operation's, and to arrive here it went through the facade");
    }
}
