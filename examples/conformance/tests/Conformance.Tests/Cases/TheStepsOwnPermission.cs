using System.Net;
using Conformance.Sales.Entities;
using Conformance.Sales.Mutations;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Invoker;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A step's <c>[RequirePermission]</c> holds, like a nested child's.
/// </summary>
/// <remarks>
///     <para>
///         The two forms of composition sit one step apart and share a default. A <b>nested child</b>
///         inside a mutation keeps its own permission, and the parent declares
///         <c>[AbsorbsChildPermissions]</c> to answer in its place. A composite's <b>step</b> does the
///         same: entering an internal call around it would mean exactly «do not ask for permissions»,
///         because <c>IsInternalCall</c> is read only by the authorization filters.
///     </para>
///     <para>
///         ⚠️ A composite absorbing its steps' permissions is defensible; doing it <b>silently and
///         always</b> is not. A <c>[RequirePermission]</c> written on a mutation would stop meaning
///         anything as soon as someone used it as a step, with no way to notice. The default is the
///         strict rule and absorbing is a declaration.
///     </para>
/// </remarks>
public class TheStepsOwnPermission(PostgresFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task AStepThatDemandsAPermission_IsAskedForInsideAComposite()
    {
        // ⚠️ The address must exist: the steps write a DeliveryAddress, and passing the order's id the
        // first step would fail 404 — the composite would stop there and the case would measure a missing
        // row instead of a permission.
        var addressId = await AnAddressAsync();

        var response = await PostAsync("/api/orders/guarded-steps", new
        {
            rename = new { id = addressId, street = "Via Roma 1", city = "Torino" },
            guarded = new { id = addressId, street = "Via Po 2", city = "Torino" },
        });

        response.StatusCode.Should().BeOneOf([HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden],
            "a step's permission holds like a nested child's: the composite is anonymous, "
            + "but the step that declares `conformance.address.guard` is not, and the caller does not have it");
    }

    /// <summary>And the composite can declare that it answers for its steps.</summary>
    /// <remarks>
    ///     The pair with the case above: same two steps, same caller without permissions, the only
    ///     difference <c>[AbsorbsChildPermissions]</c>. Without it, «the step's permission holds» would
    ///     also be satisfied by a check that always refuses.
    /// </remarks>
    [Fact]
    public async Task ACompositeThatDeclaresIt_AnswersForItsSteps()
    {
        var addressId = await AnAddressAsync();

        var response = await PostAsync("/api/orders/absorbing-steps", new
        {
            rename = new { id = addressId, street = "Via Roma 1", city = "Torino" },
            guarded = new { id = addressId, street = "Via Po 2", city = "Torino" },
        });

        ((int)response.StatusCode).Should().BeLessThan(400,
            "the composite declares that it absorbs its steps' permissions, so it answers in their place");
    }

    /// <summary>The control: the same mutation, invoked on its own, is refused.</summary>
    /// <remarks>
    ///     <para>
    ///         Without it, the two cases above could also be explained by «that permission is checked
    ///         nowhere». Here the mutation is neither a step nor a nested child: it is called directly
    ///         through its invoker, by the same caller without permissions, and the permission it declares
    ///         stops it. It is what isolates the variable of the two cases — being a step — and it stays
    ///         green whatever the composite does.
    ///     </para>
    ///     <para>
    ///         ⚠️ The call is in-process because <c>GuardDeliveryAddressMutation</c> has no route of its
    ///         own, by construction (<c>TheChildsOwnPermission</c>): its invoker is the only door that does
    ///         not go through a parent. Calling the nested parent's route instead would be the same case as
    ///         <c>TheChildsOwnPermission</c>, and would stay green even with absorbing that always absorbs.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheControl_TheSameMutationOnItsOwn_IsRefused()
    {
        var addressId = await AnAddressAsync();

        await using var scope = Services.CreateAsyncScope();
        var invoker = scope.ServiceProvider
            .GetRequiredService<IMutationInvoker<GuardDeliveryAddressMutation, DeliveryAddress>>();

        var result = await invoker.InvokeAsync(new GuardDeliveryAddressMutation
        {
            Id = addressId,
            Street = "Via Po 2",
            City = "Torino",
        });

        result.IsFailure.Should().BeTrue(
            "the mutation declares `conformance.address.guard` and the caller does not have it");
        result.Error.StatusCode.Should().BeOneOf([401, 403],
            $"refused for the permission, not for something else — received {result.Error.Code}");

        // And nothing was written: a refusal that arrived after the write would not be a refusal.
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var address = await db.Set<DeliveryAddress>()
            .AsNoTracking()
            .SingleAsync(a => a.PersistenceId == addressId);
        address.Street.Should().Be("Via Roma 1");
    }

    private async Task<Guid> AnOrderAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>An order with its address, and the address's id.</summary>
    /// <remarks>
    ///     The id is read from the database and not from the response: the write answers with the order's
    ///     read shape, which does not carry the child's id. The same way as
    ///     <c>TheChildWithALifeOfItsOwn</c>.
    /// </remarks>
    private async Task<Guid> AnAddressAsync()
    {
        var orderId = await AnOrderAsync();

        (await PutAsync($"/api/orders/{orderId}/delivery-address", new
        {
            reference = "ORD-WITH-ADDR",
            deliveryAddress = new { street = "Via Roma 1", city = "Torino" },
        })).EnsureSuccessStatusCode();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var order = await db.Set<Order>()
            .AsNoTracking()
            .Include(o => o.DeliveryAddress)
            .SingleAsync(o => o.PersistenceId == orderId);

        return order.DeliveryAddress!.PersistenceId;
    }
}
