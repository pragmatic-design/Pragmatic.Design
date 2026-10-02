using System.Net;
using Conformance.Catalog.Entities;
using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     The invoked operation's <c>[RequirePermission]</c> holds also when it is reached through a
///     boundary's public interface.
/// </summary>
/// <remarks>
///     <para>
///         The public interface is not the road of intra-boundary calls: it is the contract <em>another</em>
///         module injects, and that is the case the permission exists to protect. An implementation that
///         entered an internal call on <b>every</b> method would mean exactly «do not ask for permissions» —
///         <c>IsInternalCall</c> is read only by the authorization filters.
///     </para>
///     <para>
///         It is the composites' rule («absorbing silently and always is not defensible») one level up.
///         The pair here is the same as <see cref="TheStepsOwnPermission" />: refused by default, and
///         granted where the caller declares <c>[AbsorbsChildPermissions]</c>.
///     </para>
/// </remarks>
public class TheBoundaryInterfaceKeepsThePermission(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<Guid> AnItemAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var item = CatalogItem.Create(4.20m);
        catalog.Set<CatalogItem>().Add(item);
        catalog.Entry(item).Property(nameof(CatalogItem.Name)).CurrentValue = $"item-{Guid.NewGuid():N}"[..12];
        await catalog.SaveChangesAsync();

        return item.PersistenceId;
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

    /// <summary>Across the boundary, the invoked operation's permission is asked for.</summary>
    [Fact]
    public async Task AcrossTheBoundary_ThePermissionOfTheInvokedOperation_IsAsked()
    {
        var response = await PostAsync("/api/orders/across-boundary", new { itemId = await AnItemAsync() });

        response.StatusCode.Should().BeOneOf([HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden],
            "the caller is anonymous and the catalog operation requires `conformance.catalogitem.guard`");
    }

    /// <summary>And the caller can declare that it answers for what it invokes.</summary>
    /// <remarks>
    ///     Without this half, «the permission holds» would also be satisfied by a check that always
    ///     refuses — the opposite error, equally wrong.
    /// </remarks>
    [Fact]
    public async Task ACallerThatDeclaresIt_AnswersForWhatItInvokes()
    {
        var response = await PostAsync("/api/orders/across-boundary-absorbed", new { itemId = await AnItemAsync() });

        ((int)response.StatusCode).Should().BeLessThan(400,
            "[AbsorbsChildPermissions] says this operation answers for what it calls");
    }

    /// <summary>
    ///     And the declaration holds on a <b>mutation</b> too, not only on an action.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The attribute means one thing on both: it covers what the body invokes, and on a mutation
    ///         its nested children too. A mutation that crosses the boundary must be able to declare that
    ///         it answers for what it calls — and the first half of this pair, the refusal, is measured by
    ///         the two cases above.
    ///     </para>
    ///     <para>
    ///         ⚠️ The mutation's own permission is still asked for: absorbing applies to what the body
    ///         invokes, not to the door one enters by.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AMutationThatDeclaresIt_AnswersForWhatItInvokes()
    {
        var orderId = await AnOrderAsync();

        var response = await PutAsync(
            $"/api/orders/{orderId}/absorbed-classification", new { itemId = await AnItemAsync() });

        ((int)response.StatusCode).Should().BeLessThan(400,
            "[AbsorbsChildPermissions] on a mutation covers what the mutation invokes");

        await using var scope = Services.CreateAsyncScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var order = await sales.Set<Order>().AsNoTracking().SingleAsync(o => o.PersistenceId == orderId);
        order.Notes.Should().Be("absorbed", "and the write happened");
    }

    /// <summary>The pair's control: the same mutation without the declaration is refused.</summary>
    /// <remarks>
    ///     <c>ClassifyOrderThroughMutation</c> makes exactly the same call and declares nothing. Without this
    ///     case, «absorbing works» would also be satisfied by absorbing that always applies.
    /// </remarks>
    [Fact]
    public async Task TheSameMutationWithoutIt_IsRefused()
    {
        var orderId = await AnOrderAsync();

        var response = await PutAsync(
            $"/api/orders/{orderId}/guarded-classification", new { itemId = await AnItemAsync() });

        response.StatusCode.Should().BeOneOf([HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden],
            "without the declaration the invoked operation's permission holds");
    }
}
