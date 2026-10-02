using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;
using Warehouse.Stock.Contracts.Events;

namespace Warehouse.IntegrationTests;

/// <summary>
///     A confirmed order is carried across the three services to <c>Shipped</c> by the fulfilment
///     process in Orders: Stock picks it, Shipping dispatches it, and each hop is a message.
/// </summary>
/// <remarks>
///     Every call goes through the gateway, as the people who make them would: the order desk, the stock
///     clerk who picks, the shipping clerk who dispatches. The levels are read from one Stock instance,
///     once, at the end, for the cache reason the other Stock tests give.
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class AnOrderTravelsToItsCarrier(WarehouseFixture warehouse)
{
    [Fact]
    public async Task PlaceConfirmPickDispatch_LeavesTheOrderShipped_TheStockMoved_AndOneShipment()
    {
        var (order, sku) = await PickedOrderAsync(quantity: 3, onHand: 10);

        await DispatchAsync(order);
        await UntilStatusAsync(order, "Shipped");

        var level = (await LevelsAsync(sku.Id)).Should().ContainSingle().Subject;
        level.GetProperty("onHand").GetInt32().Should().Be(7, "the picked quantity left the shelf as a movement");
        level.GetProperty("reserved").GetInt32().Should().Be(0, "a pick is no longer a hold");

        using var shipping = ShippingCalls.ThroughTheGateway(warehouse);
        (await ShippingCalls.ShipmentsOfAsync(shipping, order)).Should().ContainSingle();
    }

    [Fact]
    public async Task RestartingOrdersBetweenPickAndDispatch_StillEndsShipped()
    {
        var (order, _) = await PickedOrderAsync(quantity: 2, onHand: 5);

        // The process is at "awaiting dispatch" in Orders' database, and Orders goes away with it.
        await warehouse.RestartOrdersAsync();

        await DispatchAsync(order);
        await UntilStatusAsync(order, "Shipped");
    }

    /// <summary>
    ///     The control: the same <c>OrderPicked</c> delivered again — its own <c>EventId</c>, read off the
    ///     broker — makes no second shipment and does not move the order again.
    /// </summary>
    [Fact]
    public async Task TheSameOrderPickedAgain_MakesNoSecondShipment_NorMovesTheOrderTwice()
    {
        var observer = await warehouse.Broker.ObserveAsync("stock.events");
        var (order, _) = await PickedOrderAsync(quantity: 1, onHand: 4);

        var payload = await WarehouseWaits.UntilAsync(
            async () => (await warehouse.Broker.MessagesOnAsync(observer))
                .FirstOrDefault(m => m.Contains(order.ToString(), StringComparison.OrdinalIgnoreCase)),
            found => found is not null, "OrderPicked crossed the broker");
        var picked = JsonSerializer.Deserialize<OrderPicked>(payload!, JsonSerializerOptions.Web)!;
        picked.OrderId.Should().Be(order);

        await using (var scope = warehouse.StockA.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(picked);

        await WarehouseWaits.UntilAsync(() => warehouse.Broker.PendingInAsync("order-picked"),
            pending => pending == 0, "both services consumed the repeat");

        using var shipping = ShippingCalls.ThroughTheGateway(warehouse);
        (await ShippingCalls.ShipmentsOfAsync(shipping, order)).Should().ContainSingle();
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        (await OrderCalls.StatusOfAsync(desk, order)).Should().Be("Picked");
    }

    private Task<(Guid Order, (Guid Id, string Sku) Product)> PickedOrderAsync(int quantity, int onHand)
        => OrderJourney.PickedAsync(warehouse, quantity, onHand);

    private Task DispatchAsync(Guid order) => OrderJourney.DispatchAsync(warehouse, order);

    private Task UntilStatusAsync(Guid order, string status) => OrderJourney.UntilStatusAsync(warehouse, order, status);

    private async Task<JsonElement[]> LevelsAsync(Guid product)
    {
        using var manager = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        return await StockCalls.LevelsOfAsync(manager, product);
    }
}
