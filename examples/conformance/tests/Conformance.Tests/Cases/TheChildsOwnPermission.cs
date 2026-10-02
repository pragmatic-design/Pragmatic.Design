using System.Net;
using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A child's permission holds also when it is reached from the parent.
/// </summary>
/// <remarks>
///     <para>
///         <c>GuardDeliveryAddressMutation</c> declares <c>[RequirePermission]</c> and has no
///         <c>[Endpoint]</c>: the parent is the <b>only</b> way to reach it. If the permission did not hold
///         there, it would hold nowhere — a rule written and never applied, and the parent a door around it.
///     </para>
///     <para>
///         ⚠️ A nested child does not go through its own invoker: a direct call to its mapper writes it. A
///         permission check that looked only at the <b>root</b> mutation's type would ask nobody for the
///         child's. The invoker walks the tree of nested operations — composed at runtime from each child,
///         so as deep as needed — and for each one asks the same check it makes for itself.
///     </para>
///     <para>
///         ⚠️ The case's control is the last test: the same write, with a child that declares <b>no</b>
///         permission, must keep passing. Without it, a check applied to every nested write would be
///         indistinguishable from one that works.
///     </para>
/// </remarks>
public class TheChildsOwnPermission(PostgresFixture fixture) : E2ETestBase(fixture)
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

    /// <summary>
    ///     The child, bare: the mutation has a single body property, and the convention puts it on the wire
    ///     without a wrapper.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Wrapped as <c>{ id, deliveryAddress: {…} }</c>, the child would be read from the outer object,
    ///     so with an empty <c>street</c>, and a 200 would still pass unless something looks at what was
    ///     written. The <c>[NotEmpty]</c> rule on the child, there to measure the order, is what makes the
    ///     difference visible.
    /// </remarks>
    private static object AnAddress(string street) => new { id = Guid.NewGuid(), street, city = "Torino" };

    [Fact]
    public async Task AChildThatDemandsAPermission_IsRefusedToAnAnonymousCaller()
    {
        var orderId = await AnOrderAsync();

        var response = await PutAsync($"/api/orders/{orderId}/guarded-address", AnAddress("Via Roma 1"));

        // The root operation is anonymous; the child is not, and the child decides.
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401 or 403, received {(int)response.StatusCode}");
    }

    /// <summary>
    ///     And the parent can declare that it answers for it — <c>[AbsorbsChildPermissions]</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Same route, same child, same anonymous caller as the first test: the only difference is the
    ///     attribute on the parent. The case measures that, and nothing else.
    /// </remarks>
    [Fact]
    public async Task AParentThatDeclaresItAbsorbs_WritesTheGuardedChild()
    {
        var orderId = await AnOrderAsync();

        var response = await PutAsync($"/api/orders/{orderId}/absorbed-address", AnAddress("Via Roma 1"));

        // Through ReadAsync: a failure names the body, not only the code.
        await ReadAsync(response);

        // And what was written is what was sent: the response DTO does not carry the address, and a
        // 200 alone let an empty street through for as long as the body was wrapped.
        await using var scope = Services.CreateAsyncScope();
        var order = await scope.ServiceProvider.GetRequiredService<SalesDbContext>().Set<Order>()
            .AsNoTracking()
            .Include(o => o.DeliveryAddress)
            .SingleAsync(o => o.PersistenceId == orderId);
        Assert.Equal("Via Roma 1", order.DeliveryAddress?.Street);
    }

    /// <summary>
    ///     The permission is decided <b>before</b> any declarative rule.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The same body as the first test, plus a street that violates <c>[NotEmpty]</c> on the child.
    ///         If the rules spoke first, the response would be 422; it is 401 or 403 because the invoker
    ///         asks for the permissions — nested children's included — and only then runs the rules. A
    ///         caller without the permission must not even learn which rules they violated.
    ///     </para>
    ///     <para>
    ///         ⚠️ The control is the next test: the same body on the route that absorbs the permission is
    ///         refused by the rule, with 422. Without it, a child whose rule never ran would give the same
    ///         401 here — and the test would measure the rule's absence, not the order.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ThePermission_IsDecidedBeforeTheChildsRules()
    {
        var orderId = await AnOrderAsync();

        var response = await PutAsync($"/api/orders/{orderId}/guarded-address", AnAddress(street: ""));

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401 or 403 — the permission comes before the rule — received {(int)response.StatusCode}");
    }

    [Fact]
    public async Task TheControl_WhereThePermissionIsAbsorbed_TheRuleIsWhatRefuses()
    {
        var orderId = await AnOrderAsync();

        var response = await PutAsync($"/api/orders/{orderId}/absorbed-address", AnAddress(street: ""));
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422 — the child's rule is alive on this body — received {(int)response.StatusCode} {text}");
        Assert.Contains("validation.notempty", text);
    }

    [Fact]
    public async Task TheControl_AChildWithNoPermission_IsStillWritten()
    {
        var orderId = await AnOrderAsync();

        var response = await PutAsync($"/api/orders/{orderId}/delivery-address", new
        {
            reference = "ORD-WITH-ADDR",
            deliveryAddress = new { street = "Via Roma 1", city = "Torino" },
        });

        response.EnsureSuccessStatusCode();
    }
}
