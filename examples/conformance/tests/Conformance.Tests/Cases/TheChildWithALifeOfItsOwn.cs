using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     The same entity written <b>through the parent</b> and reachable <b>on its own</b> — the full case of
///     <c>[PartOf]</c>.
/// </summary>
/// <remarks>
///     <para>
///         <c>[PartOf]</c> answers two questions — «may the parent write it?» and «does it have a life of its
///         own?» — and they must not be treated as one. Otherwise the choice would be between a parent that
///         cannot write the child and a child nobody can reach: <c>PRAG0438</c> would refuse the exposed
///         mutation. <c>Exclusive = false</c> keeps them apart.
///     </para>
///     <para>
///         ⚠️ The two halves must be measured together, because their <b>coexistence</b> is the point: an
///         implementation that broke one would pass the other.
///     </para>
/// </remarks>
public class TheChildWithALifeOfItsOwn(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<(Guid OrderId, Guid AddressId)> AnOrderWithAnAddressAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        var orderId = created.GetProperty("id").GetGuid();

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

        return (orderId, order.DeliveryAddress!.PersistenceId);
    }

    private async Task<DeliveryAddress> AddressAsync(Guid addressId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        return await db.Set<DeliveryAddress>()
            .AsNoTracking()
            .SingleAsync(a => a.PersistenceId == addressId);
    }

    /// <summary>
    ///     The parent's door: the order writes it nested.
    /// </summary>
    [Fact]
    public async Task TheParentStillWritesIt()
    {
        var (_, addressId) = await AnOrderWithAnAddressAsync();

        (await AddressAsync(addressId)).Street.Should().Be("Via Roma 1");
    }

    /// <summary>
    ///     ⚠️ And its own door: the same row is reached from its own address.
    /// </summary>
    /// <remarks>
    ///     It is the half <c>PRAG0438</c> would forbid without <c>Exclusive = false</c>. It coexists with the
    ///     one above, and the coexistence is the point: neither, alone, is in question.
    /// </remarks>
    [Fact]
    public async Task AndItIsReachableOnItsOwn()
    {
        var (_, addressId) = await AnOrderWithAnAddressAsync();

        var response = await PutAsync($"/api/delivery-addresses/{addressId}", new
        {
            street = "Corso Francia 5",
            city = "Torino",
        });
        response.EnsureSuccessStatusCode();

        var address = await AddressAsync(addressId);

        address.Street.Should().Be("Corso Francia 5",
            "the child has a door of its own, and goes through its operation like any other");
    }
}
