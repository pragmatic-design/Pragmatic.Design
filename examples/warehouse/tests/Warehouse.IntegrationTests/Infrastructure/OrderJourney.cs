using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Saga;
using Warehouse.Orders.Enums;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     The steps an order takes across the services, as the people who take them would: through the
///     gateway, each with their own token.
/// </summary>
internal static class OrderJourney
{
    /// <summary>An order placed and confirmed: its id, and its product. Stock holds it permanently from here.</summary>
    public static async Task<(Guid Order, (Guid Id, string Sku) Product)> ConfirmedAsync(
        WarehouseFixture warehouse, int quantity, int onHand)
    {
        var sku = await StockCalls.InStockAsync(warehouse.StockA, onHand);
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        var order = await OrderCalls.PlacedAsync(desk, sku.Sku, quantity);

        using var confirmed = await desk.PostAsync($"api/orders/{order}/confirm", null);
        if (confirmed.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException(
                $"confirming {order} answered {(int)confirmed.StatusCode}: {await confirmed.Content.ReadAsStringAsync()}");

        return (order, sku);
    }

    /// <summary>An order placed, confirmed and picked, waiting for its carrier: its id, and its product.</summary>
    public static async Task<(Guid Order, (Guid Id, string Sku) Product)> PickedAsync(
        WarehouseFixture warehouse, int quantity, int onHand)
    {
        var (order, sku) = await ConfirmedAsync(warehouse, quantity, onHand);

        // The picker picks once Stock has heard the confirmation: until then there is nothing confirmed to
        // take, which is the 409 this retries.
        using var picker = StockCalls.ThroughTheGateway(warehouse, StockCalls.Clerk);
        await WarehouseWaits.UntilAsync(async () =>
            {
                using var picked = await picker.PostAsJsonAsync("api/picks", new { orderId = order });
                return picked.StatusCode;
            },
            status => status == HttpStatusCode.Created, "the picker picked the order");

        await UntilStatusAsync(warehouse, order, "Picked");
        return (order, sku);
    }

    /// <summary>The shipping clerk dispatches the order's one shipment, once Shipping has made it.</summary>
    public static async Task DispatchAsync(WarehouseFixture warehouse, Guid order)
    {
        using var shipping = ShippingCalls.ThroughTheGateway(warehouse);
        var shipment = (await WarehouseWaits.UntilAsync(() => ShippingCalls.ShipmentsOfAsync(shipping, order),
            rows => rows.Length == 1, "Shipping made the shipment"))[0];

        using var dispatched = await shipping.PostAsJsonAsync(
            $"api/shipments/{shipment.GetProperty("id").GetGuid()}/dispatch", new { carrier = "Bartolini", packages = 1 });
        if (dispatched.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException(
                $"dispatching answered {(int)dispatched.StatusCode}: {await dispatched.Content.ReadAsStringAsync()}");
    }

    public static async Task UntilStatusAsync(WarehouseFixture warehouse, Guid order, string status)
    {
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        await WarehouseWaits.UntilAsync(() => OrderCalls.StatusOfAsync(desk, order), s => s == status,
            $"order {order} is {status}");
    }

    /// <summary>Where the order's fulfilment process is, read from Orders' database; null when it never started.</summary>
    public static async Task<Fulfilment?> ProcessOfAsync(WarehouseFixture warehouse, Guid order)
    {
        await using var scope = warehouse.Orders.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var correlation = order.ToString();

        // The boundary's own context by type: this host registers no plain DbContext alias.
        var instance = await scope.ServiceProvider.GetRequiredService<Warehouse.Orders.Entities.OrdersDbContext>()
            .Set<SagaInstance>()
            .AsNoTracking()
            .SingleOrDefaultAsync(saga => saga.CorrelationId == correlation);

        return instance is null ? null : (Fulfilment)instance.State;
    }
}
