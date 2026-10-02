using System.Net;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     <c>[SubBoundary(Name = "…")]</c>: the author decides the group, where the namespace decides nothing.
/// </summary>
/// <remarks>
///     <para>
///         <c>RenameOrderMutation</c> sits in <c>Conformance.Sales.Mutations</c>, flat: inference produces no
///         group, so without the attribute the operation would sit on the root.
///         <c>CallThroughTheDeclaredGroupAction</c> reaches it as <c>_sales.References.RenameOrder(…)</c>, a
///         path that exists only because someone wrote the name.
///     </para>
///     <para>
///         ⚠️ The attribute is how an author overrides inference: someone writing it because the namespace
///         inferred the wrong group must get the declared group, not the wrong one and silence.
///     </para>
///     <para>
///         ⚠️ <b>How it is measured.</b> Removing the attribute does not turn this case red: it turns red the
///         <em>compilation</em> of <c>CallThroughTheDeclaredGroupAction</c>, because <c>References</c> stops
///         existing. This case measures the other half — that the call through the group really reaches the
///         operation — because «compiles» and «works» are two things.
///     </para>
/// </remarks>
public class TheGroupTheAuthorNamed(PostgresFixture fixture) : E2ETestBase(fixture)
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

    [Fact]
    public async Task TheCallThroughTheDeclaredGroup_ReachesTheOperation()
    {
        var id = await AnOrderAsync();
        var reference = $"VIA-{Guid.NewGuid():N}"[..12];

        var response = await PostAsync("/api/orders/through-the-declared-group", new { orderId = id, reference });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the action invokes the mutation through _sales.References and has nothing to return");

        var order = await ReadAsync(await Client.GetAsync($"/api/orders/{id}"));
        order.GetProperty("reference").GetString().Should().Be(reference,
            "the row changed: the call went through the group and reached the mutation");
    }

    /// <summary>
    ///     The control: an order that does not exist fails, so the route does not answer empty-handed.
    /// </summary>
    /// <remarks>
    ///     Without it, «204» is also satisfied by a <c>VoidDomainAction</c> that invokes nothing — returning
    ///     <c>Success</c> gives the same 204. The failure can reach here only through the group and the
    ///     mutation's invoker.
    /// </remarks>
    [Fact]
    public async Task AnOrderThatDoesNotExist_FailsTheCall()
    {
        var response = await PostAsync("/api/orders/through-the-declared-group", new
        {
            orderId = Guid.NewGuid(),
            reference = "NEVER",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the invoked mutation does not find the row, and the error comes back along the same road");
    }
}
