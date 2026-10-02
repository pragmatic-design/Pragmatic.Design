using Conformance.Catalog.Entities;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A <c>[MapProperty]</c> that crosses the boundary where the boundary reads over there.
/// </summary>
/// <remarks>
///     <para>
///         <c>TheBoundaryAsAWall</c> measures that the navigation exists; the generator tests measure that
///         <c>PRAG0334</c> fires without <c>[ReadAccess]</c>. This is the piece in between: a DTO that
///         <b>crosses</b> that navigation, in memory and in SQL.
///     </para>
///     <para>
///         ⚠️ The item is assigned through EF's API, because <c>SetItemId</c> is <c>internal</c> to the
///         module and no conformance mutation writes that link: here the read is measured, not the write.
///     </para>
/// </remarks>
public class ThePathAcrossTheBoundary(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<Guid> AnOrderOfAsync(Guid itemId)
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        var orderId = created.GetProperty("id").GetGuid();

        await using var scope = Services.CreateAsyncScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var order = await sales.Set<Order>().SingleAsync(o => o.PersistenceId == orderId);
        sales.Entry(order).Property(nameof(Order.ItemId)).CurrentValue = itemId;
        await sales.SaveChangesAsync();

        return orderId;
    }

    private async Task<Guid> AnItemNamedAsync(string name)
    {
        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var item = CatalogItem.Create(12.50m);
        catalog.Set<CatalogItem>().Add(item);
        catalog.Entry(item).Property(nameof(CatalogItem.Name)).CurrentValue = name;
        await catalog.SaveChangesAsync();

        return item.PersistenceId;
    }

    /// <summary>The projection crosses the boundary: a JOIN on the DbSet that <c>[ReadAccess]</c> adds.</summary>
    [Fact]
    public async Task TheProjection_ReadsThroughTheNavigation()
    {
        var orderId = await AnOrderOfAsync(await AnItemNamedAsync("hammer"));

        await using var scope = Services.CreateAsyncScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var dto = await sales.Set<Order>()
            .Where(o => o.PersistenceId == orderId)
            .Select(OrderWithItemDto.Projection)
            .SingleAsync();

        dto.ItemName.Should().Be("hammer", "Item.Name is translated to SQL through the generated navigation");
    }

    /// <summary>And <c>FromEntity</c> reads it from the loaded entity, with the navigation the DTO declares.</summary>
    [Fact]
    public async Task FromEntity_ReadsThroughTheNavigation()
    {
        OrderWithItemDto.RequiredNavigations.Should().Contain("Item",
            "the read list names the crossed navigation, like any other");

        var orderId = await AnOrderOfAsync(await AnItemNamedAsync("nails"));

        await using var scope = Services.CreateAsyncScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var order = await sales.Set<Order>()
            .AsNoTracking()
            .Include(o => o.Item)
            .SingleAsync(o => o.PersistenceId == orderId);

        OrderWithItemDto.FromEntity(order).ItemName.Should().Be("nails");
    }
}
