using Conformance.Sales;
using Conformance.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Pipeline;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A declared query is invoked <b>by name</b> through its boundary's interface, like every other
///     operation.
/// </summary>
/// <remarks>
///     <para>
///         Without a member on the facade, reading a query from code takes three pieces — build the object,
///         get a source, choose the executor's overload — and a read the neighbouring module cannot call by
///         name is a read that module rewrites by hand.
///     </para>
///     <para>
///         ⚠️ The member goes through the query's <b>invoker</b>, not the executor: otherwise the three
///         pieces would only be hidden behind a name, and validation and the permission would still be
///         skipped.
///     </para>
///     <para>
///         Demonstrates: the same pair as for actions holds for a read, and the public facade does not
///         absorb the permission of what it invokes.
///     </para>
/// </remarks>
public class TheQueryHasAName(PostgresFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>Through the public facade, the query's permission is asked for.</summary>
    [Fact]
    public async Task ThePublicFacade_AsksForTheQuerysPermission()
    {
        await using var scope = Services.CreateAsyncScope();
        var sales = scope.ServiceProvider.GetRequiredService<ISalesActions>();

        var answer = await sales.GuardedOrderCount("ORD", 1, 20);

        answer.IsFailure.Should().BeTrue(
            "the caller is anonymous and the query requires `conformance.order.guardedread`");
    }

    /// <summary>The control: inside an internal call, the same facade reads.</summary>
    /// <remarks>
    ///     <para>
    ///         Without this half «the permission holds» would be satisfied by a facade that refuses everyone,
    ///         and the read by name would be unusable from inside the boundary.
    ///     </para>
    ///     <para>
    ///         ⚠️ Measured by entering the internal call by hand, not through <c>ISalesInternalActions</c>:
    ///         that interface is <c>internal</c> to the module — it is the road of whoever is already inside
    ///         the boundary — and a test cannot name it. The difference between the two facades is exactly
    ///         that line, and this case exercises it from the side a test can reach.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task InAnInternalCall_TheSameFacadeReads()
    {
        await using var scope = Services.CreateAsyncScope();
        var callContext = scope.ServiceProvider.GetRequiredService<ICallContext>();
        var sales = scope.ServiceProvider.GetRequiredService<ISalesActions>();

        using (callContext.EnterInternalCall())
        {
            var answer = await sales.GuardedOrderCount("ORD", 1, 20);

            answer.IsFailure.Should().BeFalse(
                "whoever is already inside the boundary answered for the permissions on the way in");
            answer.Value.Should().NotBeNull();
        }
    }

    /// <summary>And the whole object is accepted as a mutation's would be.</summary>
    /// <remarks>
    ///     The two overloads exist for actions and mutations, and a query is no exception: whoever already
    ///     has the query built — because they received it — should not have to break it into parameters.
    /// </remarks>
    [Fact]
    public async Task TheOverloadTakingTheObject_ExistsAndIsTheSame()
    {
        await using var scope = Services.CreateAsyncScope();
        var callContext = scope.ServiceProvider.GetRequiredService<ICallContext>();
        var sales = scope.ServiceProvider.GetRequiredService<ISalesActions>();

        using (callContext.EnterInternalCall())
        {
            var answer = await sales.GuardedOrderCount(
                new Conformance.Sales.Queries.GuardedOrderCountQuery { Reference = "ORD" });

            answer.IsFailure.Should().BeFalse();
        }
    }
}
