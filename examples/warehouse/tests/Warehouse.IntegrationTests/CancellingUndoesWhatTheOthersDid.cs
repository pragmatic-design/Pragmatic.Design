using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;
using Warehouse.Orders.Contracts.Events;
using Warehouse.Orders.Enums;
using Warehouse.Stock.Entities;

namespace Warehouse.IntegrationTests;

/// <summary>
///     Cancelling an order before it ships undoes what the other services already did: what
///     Stock held is released, what it picked goes back on its shelf, and the shipment is withdrawn.
/// </summary>
/// <remarks>
///     The levels are read from one Stock instance, once, after Stock has recorded that it restored the
///     order — the availability read is cached per instance, and polling it would read the cache.
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class CancellingUndoesWhatTheOthersDid(WarehouseFixture warehouse)
{
    private const string StockCancellations = "stock.order-cancelled";

    [Fact]
    public async Task CancellingAReservedOrder_RestoresAvailabilityExactly()
    {
        var sku = await StockCalls.InStockAsync(warehouse.StockA, 10);
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        var order = await OrderCalls.PlacedAsync(desk, sku.Sku, 4);

        await CancelAsync(desk, order);
        await UntilRestoredAsync(order);

        var level = (await LevelsAsync(sku.Id)).Single();
        level.GetProperty("reserved").GetInt32().Should().Be(0);
        level.GetProperty("available").GetInt32().Should().Be(10, "availability is exactly what it was before the order");
    }

    [Fact]
    public async Task CancellingAPickedOrder_PutsTheStockBack_WithdrawsTheShipment_AndTheProcessIsCompensated()
    {
        var (order, sku) = await OrderJourney.PickedAsync(warehouse, quantity: 3, onHand: 10);
        using var shipping = ShippingCalls.ThroughTheGateway(warehouse);
        await WarehouseWaits.UntilAsync(() => ShippingCalls.ShipmentsOfAsync(shipping, order),
            rows => rows.Length == 1, "Shipping made the shipment");

        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        await CancelAsync(desk, order);

        await WarehouseWaits.UntilAsync(() => OrderJourney.ProcessOfAsync(warehouse, order),
            state => state == Fulfilment.Compensated, "both services acknowledged, and the process is compensated");

        (await ShippingCalls.ShipmentsOfAsync(shipping, order)).Single()
            .GetProperty("status").GetString().Should().Be("Withdrawn");
        var level = (await LevelsAsync(sku.Id)).Single();
        level.GetProperty("onHand").GetInt32().Should().Be(10, "the picked goods came back with a return movement");
        level.GetProperty("reserved").GetInt32().Should().Be(0);
    }

    /// <summary>
    ///     The same cancellation delivered again — its own <c>EventId</c>, read off the broker — puts nothing
    ///     back a second time.
    /// </summary>
    [Fact]
    public async Task TheSameCancellationDeliveredTwice_IsAppliedOnce()
    {
        var observer = await warehouse.Broker.ObserveAsync("orders.events");
        var (order, sku) = await OrderJourney.PickedAsync(warehouse, quantity: 3, onHand: 10);
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        await CancelAsync(desk, order);
        await UntilRestoredAsync(order);

        var payload = await WarehouseWaits.UntilAsync(
            async () => (await warehouse.Broker.MessagesOfTypeOnAsync(observer, nameof(OrderCancelled)))
                .FirstOrDefault(m => m.Contains(order.ToString(), StringComparison.OrdinalIgnoreCase)),
            found => found is not null, "OrderCancelled crossed the broker");
        var cancelled = JsonSerializer.Deserialize<OrderCancelled>(payload!, JsonSerializerOptions.Web)!;

        var consumed = await warehouse.Broker.AcknowledgedOnAsync(StockCancellations);
        await using (var scope = warehouse.Orders.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(cancelled);
        await WarehouseWaits.UntilAsync(() => warehouse.Broker.AcknowledgedOnAsync(StockCancellations),
            acked => acked > consumed, "Stock consumed the repeat");

        (await LevelsAsync(sku.Id)).Single().GetProperty("onHand").GetInt32()
            .Should().Be(10, "the goods came back once, not twice");
    }

    /// <summary>The control: a shipped order is not cancelled, and no service changes anything.</summary>
    [Fact]
    public async Task CancellingAShippedOrder_Is409_AndNothingChangesAnywhere()
    {
        var (order, sku) = await OrderJourney.PickedAsync(warehouse, quantity: 3, onHand: 10);
        await OrderJourney.DispatchAsync(warehouse, order);
        await OrderJourney.UntilStatusAsync(warehouse, order, "Shipped");

        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        using var refused = await desk.PostAsync($"api/orders/{order}/cancel", null);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, "Shipped → Cancelled is not a move");

        (await OrderCalls.StatusOfAsync(desk, order)).Should().Be("Shipped");
        using var shipping = ShippingCalls.ThroughTheGateway(warehouse);
        (await ShippingCalls.ShipmentsOfAsync(shipping, order)).Single()
            .GetProperty("status").GetString().Should().Be("Dispatched");
        (await RestorationOfAsync(order)).Should().BeNull("Stock was never asked to undo anything");
        (await LevelsAsync(sku.Id)).Single().GetProperty("onHand").GetInt32().Should().Be(7);
    }

    private static async Task CancelAsync(HttpClient desk, Guid order)
    {
        using var cancelled = await desk.PostAsync($"api/orders/{order}/cancel", null);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
    }

    private async Task UntilRestoredAsync(Guid order)
        => await WarehouseWaits.UntilAsync(() => RestorationOfAsync(order), found => found is not null,
            "Stock restored the order's stock");

    /// <summary>Stock's record that it undid its part of the order, read from Stock's database.</summary>
    private async Task<StockRestoration?> RestorationOfAsync(Guid order)
    {
        await using var scope = warehouse.StockA.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DbContext>().Set<StockRestoration>()
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.OrderId == order);
    }

    private async Task<JsonElement[]> LevelsAsync(Guid product)
    {
        using var manager = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        return await StockCalls.LevelsOfAsync(manager, product);
    }
}
