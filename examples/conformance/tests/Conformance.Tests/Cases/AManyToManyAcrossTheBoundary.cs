using Conformance.Catalog.Entities;
using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A many-to-many to an entity the boundary reads, written and read back through its join table.
/// </summary>
/// <remarks>
///     <para>
///         <c>Order</c> declares <c>[Relation.ManyToMany&lt;CatalogItem&gt;.WithNavigation("Suggestions")]</c>, and
///         <c>CatalogItem</c> is Catalog's, read by <c>SalesBoundary</c> with <c>[ReadAccess&lt;CatalogItem&gt;]</c>.
///         The migration creates the join table on Sales' side.
///     </para>
///     <para>
///         ⚠️ The entity configuration skipped every navigation into another boundary and left it to EF Core's
///         conventions, readable or not. With no inverse on the other side, convention reads a collection as a
///         one-to-many: a shadow <c>OrderPersistenceId</c> on Catalog's table, which no migration creates. The
///         schema had the join table and the model looked for a column, so every read of an <c>Order</c> failed
///         with <c>42703</c>, not only the reads that named the navigation.
///     </para>
/// </remarks>
public class AManyToManyAcrossTheBoundary(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<Guid> AnItemOwnedByCatalogAsync(decimal price)
    {
        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var item = CatalogItem.Create(price);
        catalog.Set<CatalogItem>().Add(item);
        await catalog.SaveChangesAsync();

        return item.PersistenceId;
    }

    [Fact]
    public async Task AnOrderLinkedToItemsItReads_IsWrittenAndReadBackWithItsLinks()
    {
        var first = await AnItemOwnedByCatalogAsync(3.00m);
        var second = await AnItemOwnedByCatalogAsync(4.00m);

        Guid orderId;
        await using (var scope = Services.CreateAsyncScope())
        {
            var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            var items = await sales.Set<CatalogItem>()
                .Where(i => i.PersistenceId == first || i.PersistenceId == second)
                .ToListAsync();

            var order = Order.Create();
            sales.Entry(order).Property(nameof(Order.Reference)).CurrentValue = "M2M-ACROSS";
            sales.Entry(order).Property(nameof(Order.ItemId)).CurrentValue = first;
            foreach (var item in items)
                order.Suggestions.Add(item);

            sales.Set<Order>().Add(order);
            await sales.SaveChangesAsync();
            orderId = order.PersistenceId;
        }

        await using var read = Services.CreateAsyncScope();
        var reader = read.ServiceProvider.GetRequiredService<SalesDbContext>();

        var stored = await reader.Set<Order>().AsNoTracking()
            .Include(o => o.Suggestions)
            .SingleAsync(o => o.PersistenceId == orderId);

        stored.Suggestions.Select(i => i.PersistenceId).Should().BeEquivalentTo([first, second],
            "the links were written to the join table and are read back through it");
    }

    /// <summary>The control: an order with no links reads back with none, through the same model.</summary>
    [Fact]
    public async Task AnOrderWithoutLinks_ReadsBackWithNone()
    {
        var item = await AnItemOwnedByCatalogAsync(5.00m);

        Guid orderId;
        await using (var scope = Services.CreateAsyncScope())
        {
            var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            var order = Order.Create();
            sales.Entry(order).Property(nameof(Order.Reference)).CurrentValue = "M2M-NONE";
            sales.Entry(order).Property(nameof(Order.ItemId)).CurrentValue = item;
            sales.Set<Order>().Add(order);
            await sales.SaveChangesAsync();
            orderId = order.PersistenceId;
        }

        await using var read = Services.CreateAsyncScope();
        var stored = await read.ServiceProvider.GetRequiredService<SalesDbContext>().Set<Order>().AsNoTracking()
            .Include(o => o.Suggestions)
            .SingleAsync(o => o.PersistenceId == orderId);

        stored.Suggestions.Should().BeEmpty();
    }
}
