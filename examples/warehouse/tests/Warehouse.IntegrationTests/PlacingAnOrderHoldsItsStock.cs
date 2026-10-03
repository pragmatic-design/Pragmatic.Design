using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;

namespace Warehouse.IntegrationTests;

/// <summary>
///     Placing an order asks Stock, over the broker, to hold every line, and answers the
///     customer with the outcome: reserved, short, or Stock not answering.
/// </summary>
/// <remarks>
///     <para>
///         The orders go through the gateway, and the request reaches whichever Stock instance takes it
///         off the queue. The levels are read from one instance, once, after the placing: the
///         availability read is cached per instance and the other instance's drop arrives over Redis a
///         moment after the save, so a read made earlier on that instance could still be served.
///     </para>
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class PlacingAnOrderHoldsItsStock(WarehouseFixture warehouse)
{
    [Fact]
    public async Task AnOrderForStockThatIsThere_IsReserved_AndTheStockIsHeld()
    {
        var sku = await InStockAsync(10);
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        var order = await DraftAsync(desk, (sku.Sku, 4));

        using var placed = await desk.PostAsync($"api/orders/{order}/place", null);

        var body = await placed.Content.ReadAsStringAsync();
        placed.StatusCode.Should().Be(HttpStatusCode.OK, body);
        JsonDocument.Parse(body).RootElement.GetProperty("status").GetString().Should().Be("Reserved");
        (await OrderCalls.ReadAsync(desk, order)).GetProperty("status").GetString().Should().Be("Reserved");

        var level = (await LevelsAsync(sku.Id)).Should().ContainSingle().Subject;
        level.GetProperty("onHand").GetInt32().Should().Be(10, "a hold takes nothing off the shelf");
        level.GetProperty("reserved").GetInt32().Should().Be(4);
        level.GetProperty("available").GetInt32().Should().Be(6);
    }

    [Fact]
    public async Task AnOrderForMoreThanIsThere_Is409NamingTheLine_AndHoldsNothing()
    {
        var plenty = await InStockAsync(10);
        var scarce = await InStockAsync(3);
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        var order = await DraftAsync(desk, (plenty.Sku, 2), (scarce.Sku, 5));

        using var refused = await desk.PostAsync($"api/orders/{order}/place", null);

        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain("INSUFFICIENT_STOCK");
        body.Should().Contain(scarce.Sku, "the answer names the line that is short");
        body.Should().NotContain(plenty.Sku, "the line that could be held is not short");

        (await OrderCalls.ReadAsync(desk, order)).GetProperty("status").GetString().Should().Be("Draft");
        (await LevelsAsync(plenty.Id)).Single().GetProperty("reserved").GetInt32()
            .Should().Be(0, "all the lines or none: the one that fitted is not held either");
        (await LevelsAsync(scarce.Id)).Single().GetProperty("reserved").GetInt32().Should().Be(0);
    }

    /// <summary>
    ///     The control: the refusal is about what is left, not about the order. Two orders that together
    ///     exceed the stock — the first is held, the second is refused, and the first keeps its hold.
    /// </summary>
    [Fact]
    public async Task TwoOrdersThatTogetherExceedTheStock_TheSecondIsRefused_TheFirstKeepsItsHold()
    {
        var sku = await InStockAsync(5);
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        var first = await DraftAsync(desk, (sku.Sku, 3));
        var second = await DraftAsync(desk, (sku.Sku, 3));

        using var held = await desk.PostAsync($"api/orders/{first}/place", null);
        held.StatusCode.Should().Be(HttpStatusCode.OK, await held.Content.ReadAsStringAsync());

        using var refused = await desk.PostAsync($"api/orders/{second}/place", null);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());

        (await OrderCalls.ReadAsync(desk, first)).GetProperty("status").GetString().Should().Be("Reserved");
        (await LevelsAsync(sku.Id)).Single().GetProperty("reserved").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task AnOrderAlreadyReserved_IsNotPlacedAgain()
    {
        var sku = await InStockAsync(10);
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        var order = await DraftAsync(desk, (sku.Sku, 2));
        using var placed = await desk.PostAsync($"api/orders/{order}/place", null);
        placed.StatusCode.Should().Be(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());

        using var again = await desk.PostAsync($"api/orders/{order}/place", null);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict, "Reserved → Placed is not a move");
        (await LevelsAsync(sku.Id)).Single().GetProperty("reserved").GetInt32()
            .Should().Be(2, "the refused second placing asked Stock for nothing");
    }

    [Fact]
    public async Task WithNoStockToAnswer_PlacingIs503_WithinTheTimeout_AndTheOrderStaysADraft()
    {
        await using var orders = warehouse.OrdersWithNobodyToAnswer();
        using var desk = OrderCalls.ToInstance(orders);
        var order = await DraftAsync(desk, ("SKU-NOBODY-HOLDS", 1));

        var clock = Stopwatch.StartNew();
        using var unanswered = await desk.PostAsync($"api/orders/{order}/place", null);
        clock.Stop();

        var body = await unanswered.Content.ReadAsStringAsync();
        unanswered.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, $"{body}{Environment.NewLine}{orders.Errors}");
        body.Should().Contain("STOCK_UNANSWERED");
        clock.Elapsed.Should().BeLessThan(WarehouseFixture.RequestReplyTimeout + TimeSpan.FromSeconds(5),
            "the customer is told when the timeout runs out, not after a hang");
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(WarehouseFixture.RequestReplyTimeout - TimeSpan.FromMilliseconds(100),
            "the 503 is the timeout, not a failure before the request was sent");

        (await OrderCalls.ReadAsync(desk, order)).GetProperty("status").GetString().Should().Be("Draft");
    }

    private Task<(Guid Id, string Sku)> InStockAsync(int quantity) => StockCalls.InStockAsync(warehouse.StockA, quantity);

    private async Task<JsonElement[]> LevelsAsync(Guid product)
    {
        using var manager = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        return await StockCalls.LevelsOfAsync(manager, product);
    }

    private static async Task<Guid> DraftAsync(HttpClient desk, params (string Sku, int Quantity)[] lines)
    {
        using var created = await OrderCalls.DraftAsync(desk, lines);
        var body = await created.Content.ReadAsStringAsync();
        created.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }
}
