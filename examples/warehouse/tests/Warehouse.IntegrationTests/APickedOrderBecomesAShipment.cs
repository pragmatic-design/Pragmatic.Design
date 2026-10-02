using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;
using Warehouse.Stock.Contracts.Events;

namespace Warehouse.IntegrationTests;

/// <summary>
///     A picked order becomes one shipment however many times the event arrives, and a
///     dispatched shipment says so once, with its tracking number.
/// </summary>
/// <remarks>
///     <c>OrderPicked</c> is published here from a Stock instance's bus, as Stock itself publishes it when
///     an order is picked. Shipping is told the lines on the event and reads nobody's database.
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class APickedOrderBecomesAShipment(WarehouseFixture warehouse)
{
    private const string ShippingEvents = "shipping.events";

    [Fact]
    public async Task APickedOrder_BecomesOneShipment_EvenWhenTheEventArrivesTwice()
    {
        using var clerk = ShippingCalls.ThroughTheGateway(warehouse);
        var picked = Picked(("SKU-A", 2), ("SKU-B", 1));

        await ShippingCalls.PublishAsync(warehouse, picked);
        await ShippingCalls.PublishAsync(warehouse, picked);

        // Both deliveries consumed — the queue is empty — and only then is "one" an observation.
        await WarehouseWaits.UntilAsync(() => ShippingCalls.ShipmentsOfAsync(clerk, picked.OrderId),
            rows => rows.Length > 0, "the shipment was made");
        await WarehouseWaits.UntilAsync(() => warehouse.Broker.PendingInAsync("order-picked"),
            pending => pending == 0, "both deliveries were consumed");

        var shipment = (await ShippingCalls.ShipmentsOfAsync(clerk, picked.OrderId))
            .Should().ContainSingle("the same event delivered twice makes one shipment").Subject;
        shipment.GetProperty("status").GetString().Should().Be("Created");
        shipment.GetProperty("trackingNumber").GetString().Should().StartWith("TRK-");
        shipment.GetProperty("lines").EnumerateArray()
            .Select(line => (line.GetProperty("sku").GetString(), line.GetProperty("quantity").GetInt32()))
            .OrderBy(line => line.Item1)
            .Should().Equal([("SKU-A", 2), ("SKU-B", 1)]);
    }

    /// <summary>
    ///     The control for the idempotence: a second picking of the same order is a different event, and
    ///     it is not mistaken for a repeat.
    /// </summary>
    [Fact]
    public async Task TwoDifferentEvents_AreTwoShipments()
    {
        using var clerk = ShippingCalls.ThroughTheGateway(warehouse);
        var first = Picked(("SKU-A", 1));
        // A new event and not `first with { … }`, which would copy the EventId and be the same event.
        var second = new OrderPicked(first.OrderId, [new PickedLine("SKU-C", 3)], DateTimeOffset.UtcNow);
        second.EventId.Should().NotBe(first.EventId);

        await ShippingCalls.PublishAsync(warehouse, first);
        await ShippingCalls.PublishAsync(warehouse, second);

        await WarehouseWaits.UntilAsync(() => ShippingCalls.ShipmentsOfAsync(clerk, first.OrderId),
            rows => rows.Length == 2, "two events made two shipments");
    }

    [Fact]
    public async Task Dispatching_PublishesShipmentDispatchedOnce_WithTheTrackingNumber_AndASecondDispatchIs409()
    {
        using var clerk = ShippingCalls.ThroughTheGateway(warehouse);
        var picked = Picked(("SKU-A", 4));
        await ShippingCalls.PublishAsync(warehouse, picked);
        var shipment = (await WarehouseWaits.UntilAsync(() => ShippingCalls.ShipmentsOfAsync(clerk, picked.OrderId),
            rows => rows.Length == 1, "the shipment was made"))[0];
        var id = shipment.GetProperty("id").GetGuid();
        var tracking = shipment.GetProperty("trackingNumber").GetString()!;

        var observer = await warehouse.Broker.ObserveAsync(ShippingEvents);

        using var dispatched = await clerk.PostAsJsonAsync($"api/shipments/{id}/dispatch", new { carrier = "Bartolini", packages = 2 });
        var body = await dispatched.Content.ReadAsStringAsync();
        dispatched.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var answer = JsonDocument.Parse(body).RootElement;
        answer.GetProperty("status").GetString().Should().Be("Dispatched");
        answer.GetProperty("carrier").GetString().Should().Be("Bartolini");

        var published = await WarehouseWaits.UntilAsync(() => MessagesAboutAsync(observer, picked.OrderId),
            messages => messages.Count > 0, "ShipmentDispatched was published");
        published.Should().ContainSingle().Which.Should().Contain(tracking, "the event carries the tracking number");

        using var again = await clerk.PostAsJsonAsync($"api/shipments/{id}/dispatch", new { carrier = "Bartolini", packages = 2 });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict, "Dispatched → Dispatched is not a move");

        // Two outbox polls later there is still one: the refused dispatch saved nothing to publish.
        await Task.Delay(TimeSpan.FromSeconds(2));
        (await MessagesAboutAsync(observer, picked.OrderId)).Should().ContainSingle();
    }

    private async Task<List<string>> MessagesAboutAsync(string queue, Guid orderId)
        => [.. (await warehouse.Broker.MessagesOnAsync(queue)).Where(m => m.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase))];

    private static OrderPicked Picked(params (string Sku, int Quantity)[] lines)
        => new(Guid.NewGuid(), [.. lines.Select(line => new PickedLine(line.Sku, line.Quantity))], DateTimeOffset.UtcNow);
}
