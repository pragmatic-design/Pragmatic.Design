using System.Net;
using Conformance.Sales.Queries;
using Conformance.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Pipeline;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A declared query is invoked one way only, and that way validates the input and asks for the
///     permission.
/// </summary>
/// <remarks>
///     <para>
///         Without the invoker the two declarations would hold only on the route: the generated handler
///         validating on its own and the permission sitting on the route builder. Reached in-process —
///         which is how another domain operation reaches it — the same query would validate nothing and
///         ask nobody for anything. Two doors, two rules.
///     </para>
///     <para>
///         ⚠️ The half that matters is the <b>control</b>: «refused» is satisfied by refusing always. Here
///         the permission is granted the way the framework already knows — the internal call, the same one
///         that covers a composite's steps — and the query reads.
///     </para>
///     <para>
///         Demonstrates: a query's pipeline is an action's, minus the transaction: a read does not write,
///         and a reader that declared a commit scope would decide when the work of whoever surrounds it is
///         durable.
///     </para>
/// </remarks>
public class TheQueryGoesThroughItsInvoker(PostgresFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>In-process, without the permission, the query is refused.</summary>
    [Fact]
    public async Task InProcess_WithoutThePermission_TheQueryIsRefused()
    {
        await using var scope = Services.CreateAsyncScope();
        var invoker = new GuardedOrderCountQuery.Invoker(scope.ServiceProvider);

        var answer = await invoker.RunAsync(new GuardedOrderCountQuery { Reference = "ORD" });

        answer.IsFailure.Should().BeTrue(
            "the caller is anonymous and the query requires `conformance.order.guardedread`");
    }

    /// <summary>The control: in an internal call, the same query reads.</summary>
    /// <remarks>
    ///     Without this half «the permission holds» would be satisfied by an invoker that refuses
    ///     everyone, and every query in the repository would start answering 403.
    /// </remarks>
    [Fact]
    public async Task InAnInternalCall_TheSameQueryReads()
    {
        await using var scope = Services.CreateAsyncScope();
        var callContext = scope.ServiceProvider.GetRequiredService<ICallContext>();
        var invoker = new GuardedOrderCountQuery.Invoker(scope.ServiceProvider);

        using (callContext.EnterInternalCall())
        {
            var answer = await invoker.RunAsync(new GuardedOrderCountQuery { Reference = "ORD" });

            answer.IsFailure.Should().BeFalse(
                "the internal call is the same road that covers a composite's steps");
            answer.Value.Should().NotBeNull();
        }
    }

    /// <summary>An input validation refuses does not become rows.</summary>
    /// <remarks>
    ///     Measured in an internal call, so the permission is out of the question and what remains is
    ///     validation alone: otherwise «refused» would not say by which of the two steps.
    /// </remarks>
    [Fact]
    public async Task AnInputValidationRefuses_DoesNotBecomeRows()
    {
        await using var scope = Services.CreateAsyncScope();
        var callContext = scope.ServiceProvider.GetRequiredService<ICallContext>();
        var invoker = new GuardedOrderCountQuery.Invoker(scope.ServiceProvider);

        using (callContext.EnterInternalCall())
        {
            var answer = await invoker.RunAsync(new GuardedOrderCountQuery { Reference = "X" });

            answer.IsFailure.Should().BeTrue(
                "[MinLength(3)] on the reference is a rule the query declares about itself");
        }
    }

    /// <summary>And a query's route keeps answering as before.</summary>
    /// <remarks>
    ///     Delegating to the invoker is a change of path, not of contract: without this case the rest of
    ///     the file would also be satisfied by a route that stopped answering.
    /// </remarks>
    [Fact]
    public async Task TheRoute_AnswersAsBefore()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        var response = await Client.GetAsync($"/api/orders/{created.GetProperty("id").GetGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the read goes through the invoker and answers with the same shape as before");
    }

    /// <summary>And the guarded query's route asks for the permission, as it did.</summary>
    [Fact]
    public async Task TheGuardedRoute_AsksForThePermission()
    {
        var response = await Client.GetAsync("/api/orders/guarded?reference=ORD");

        response.StatusCode.Should().BeOneOf([HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden],
            "the permission declared on the query holds on the HTTP door too");
    }
}
