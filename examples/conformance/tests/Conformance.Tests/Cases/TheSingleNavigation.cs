using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     The child that is <b>one</b>, not many.
/// </summary>
/// <remarks>
///     <para>
///         A collection has strategies, and so does a reference: «this order no longer has an address» is
///         said with <c>[ReferenceStrategy(ReferenceStrategy.Detach)]</c>.
///     </para>
///     <para>
///         ⚠️ The two write doors must agree on what a null means. Mapping's DTO wraps the call in
///         <c>if (this.X is not null)</c> — the null says nothing — and so must a <b>mutation</b>: without
///         that guard the null would reach <c>MapOneToOne</c>, which reads it as «detach», and an update that
///         omitted an optional child would <b>remove its link</b>, silently.
///     </para>
///     <para>
///         The check does not go through the response: <c>OrderDto</c> does not expose the address. It looks
///         at the database, which is where the defect would be.
///     </para>
/// </remarks>
public class TheSingleNavigation(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<Guid> AnOrderWithAnAddressAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        var orderId = created.GetProperty("id").GetGuid();

        var response = await PutAsync($"/api/orders/{orderId}/delivery-address", new
        {
            reference = "ORD-WITH-ADDR",
            deliveryAddress = new { street = "Via Roma 1", city = "Torino" },
        });
        response.EnsureSuccessStatusCode();

        return orderId;
    }

    private async Task<DeliveryAddress?> AddressOfAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var order = await db.Set<Order>()
            .AsNoTracking()
            .Include(o => o.DeliveryAddress)
            .SingleAsync(o => o.PersistenceId == orderId);

        return order.DeliveryAddress;
    }

    /// <summary>
    ///     The child arrives, with the identity it was given.
    /// </summary>
    [Fact]
    public async Task AChildThatIsOne_IsWritten()
    {
        var orderId = await AnOrderWithAnAddressAsync();

        var address = await AddressOfAsync(orderId);

        address.Should().NotBeNull();
        address!.Street.Should().Be("Via Roma 1");
        address.City.Should().Be("Torino");
    }

    /// <summary>
    ///     ⚠️ <b>Not mentioning it does not remove it</b>.
    /// </summary>
    /// <remarks>
    ///     The other case's control. Without it, an implementation that always detaches would pass.
    /// </remarks>
    [Fact]
    public async Task AnUpdateThatDoesNotMentionTheChild_LeavesItAlone()
    {
        var orderId = await AnOrderWithAnAddressAsync();

        var response = await PutAsync($"/api/orders/{orderId}/reference", new
        {
            reference = "ORD-RENAMED",
        });
        response.EnsureSuccessStatusCode();

        var address = await AddressOfAsync(orderId);

        address.Should().NotBeNull(
            "the mutation declares no [ReferenceStrategy], so Merge applies: an omitted child "
            + "means «I am not telling you about it», not «remove it»");
    }

    /// <summary>
    ///     And saying it removes it.
    /// </summary>
    /// <remarks>
    ///     What happens to the <b>row</b> is decided by the relation, not by the framework: here it is
    ///     measured, not assumed. The same division of labour <c>CollectionStrategy.Sync</c> has.
    /// </remarks>
    [Fact]
    public async Task ANullUnderDetach_RemovesTheLink()
    {
        var orderId = await AnOrderWithAnAddressAsync();

        var response = await PutAsync($"/api/orders/{orderId}/delivery-address", new
        {
            reference = "ORD-NO-ADDR",
            deliveryAddress = (object?)null,
        });
        response.EnsureSuccessStatusCode();

        var address = await AddressOfAsync(orderId);

        address.Should().BeNull("[ReferenceStrategy(Detach)] lets the null reach MapOneToOne");
    }
}
