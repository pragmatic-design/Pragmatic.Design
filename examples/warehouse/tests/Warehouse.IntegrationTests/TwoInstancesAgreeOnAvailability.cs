using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;
using Warehouse.Stock.Entities;

namespace Warehouse.IntegrationTests;

/// <summary>
///     The two Stock instances agree on availability: a movement on one drops what the other has
///     cached.
/// </summary>
/// <remarks>
///     <para>
///         Every call here is addressed to one instance by its own address, not through the gateway, whose
///         balancing would decide which instance answers and make "A cached, B wrote" a coin toss.
///     </para>
///     <para>
///         The availability read is <c>[Cacheable]</c> and each instance keeps an in-process copy. What
///         Redis adds is the channel: an invalidation run on one instance is broadcast to every instance
///         (<c>AddRedisCacheInvalidationBroadcast</c>). Without it, A answers what it cached before B's
///         receipt until the entry expires.
///     </para>
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class TwoInstancesAgreeOnAvailability(WarehouseFixture warehouse)
{
    [Fact]
    public async Task AReceiptOnB_IsWhatAReadsNext_NotWhatACached()
    {
        var product = await StockCalls.InStockAsync(warehouse.StockA, 10);
        using var a = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        using var b = StockCalls.ToInstance(warehouse.StockB, StockCalls.Manager);

        (await OnHandAsync(a, product.Id)).Should().Be(10, "the first read, which A caches");

        using var received = await b.PostAsJsonAsync("api/levels/receipts",
            new { productId = product.Id, locationId = await LocationOfAsync(product.Id), quantity = 5 });
        received.StatusCode.Should().Be(HttpStatusCode.Created, await received.Content.ReadAsStringAsync());

        // The broadcast crosses Redis, so A drops its copy shortly after B's save, not at the same instant.
        await WarehouseWaits.UntilAsync(() => OnHandAsync(a, product.Id), onHand => onHand == 15,
            "A reads the level B's receipt made, not the one it cached");
    }

    /// <summary>
    ///     The control: without a movement, A's second read is its cache — a change written past the
    ///     operations, which invalidates nothing, is not seen.
    /// </summary>
    /// <remarks>
    ///     Written through the database for that reason: the operations are what invalidate, so using one
    ///     would race the very broadcast this suite is about. A cached answer is distinguishable from a
    ///     fresh one only when the row changed and nobody said so.
    /// </remarks>
    [Fact]
    public async Task WithoutAMovement_ASecondReadOnA_IsServedFromItsCache()
    {
        var product = await StockCalls.InStockAsync(warehouse.StockA, 10);
        using var a = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        (await OnHandAsync(a, product.Id)).Should().Be(10);

        await using (var scope = warehouse.StockA.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DbContext>().Set<StockLevel>()
                .Where(level => level.ProductId == product.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(level => level.OnHand, 99));

        (await OnHandAsync(a, product.Id)).Should().Be(10, "nothing invalidated the entry, so A did not query");
    }

    /// <summary>
    ///     A movement drops the availability of the products it moved, and of no other.
    /// </summary>
    /// <remarks>
    ///     The control above rests on this. With one tag for every product, any movement anywhere in the
    ///     suite — another test's receipt, or the expiry job of an order placed seconds earlier — dropped
    ///     every entry, and the control saw the database whenever one landed between its two reads.
    /// </remarks>
    [Fact]
    public async Task AReceiptOfAnotherProduct_LeavesThisProductCached()
    {
        var product = await StockCalls.InStockAsync(warehouse.StockA, 10);
        using var a = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        (await OnHandAsync(a, product.Id)).Should().Be(10);

        await using (var scope = warehouse.StockA.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DbContext>().Set<StockLevel>()
                .Where(level => level.ProductId == product.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(level => level.OnHand, 99));

        // On A itself, so the drop is local and synchronous: no broadcast to wait for either way.
        await StockCalls.InStockAsync(warehouse.StockA, 3);

        (await OnHandAsync(a, product.Id)).Should().Be(10, "the receipt moved another product, so this entry stays");
    }

    private static async Task<int> OnHandAsync(HttpClient instance, Guid product)
        => (await StockCalls.LevelsOfAsync(instance, product)).Single().GetProperty("onHand").GetInt32();

    private async Task<Guid> LocationOfAsync(Guid product)
    {
        await using var scope = warehouse.StockA.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DbContext>().Set<StockLevel>()
            .Where(level => level.ProductId == product)
            .Select(level => level.LocationId)
            .SingleAsync();
    }
}
