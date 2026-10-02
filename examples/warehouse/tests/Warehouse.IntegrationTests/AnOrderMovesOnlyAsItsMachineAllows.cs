using System.Net;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;

namespace Warehouse.IntegrationTests;

/// <summary>
///     An order is its lines and where it has got to: the lines are checked one by one, and the
///     order moves only along the machine <c>OrderStatus</c> declares.
/// </summary>
[Collection(WarehouseCollection.Name)]
public sealed class AnOrderMovesOnlyAsItsMachineAllows(WarehouseFixture warehouse)
{
    [Fact]
    public async Task ADraftWithTwoLines_IsReadBack_WithItsLinesAndANumber()
    {
        using var desk = OrderCalls.ThroughTheGateway(warehouse);

        using var created = await OrderCalls.DraftAsync(desk, ("SKU-A", 3), ("SKU-B", 1));
        var body = await created.Content.ReadAsStringAsync();
        created.StatusCode.Should().Be(HttpStatusCode.Created, body);
        var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();

        var order = await OrderCalls.ReadAsync(desk, id);
        order.GetProperty("status").GetString().Should().Be("Draft");
        order.GetProperty("number").GetString().Should().StartWith("ORD-", "the number comes from the sequence the save draws");

        var lines = order.GetProperty("lines").EnumerateArray()
            .Select(line => (line.GetProperty("sku").GetString(), line.GetProperty("quantity").GetInt32()))
            .OrderBy(line => line.Item1)
            .ToList();
        lines.Should().Equal([("SKU-A", 3), ("SKU-B", 1)]);
    }

    [Fact]
    public async Task ALineWithQuantityZero_IsRefused_NamingTheLine()
    {
        using var desk = OrderCalls.ThroughTheGateway(warehouse);

        using var refused = await OrderCalls.DraftAsync(desk, ("SKU-A", 2), ("SKU-B", 0));

        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, body);
        FieldsIn(body).Should().ContainSingle("one line is wrong, and it is the second")
            .Which.Should().StartWith("lines[1]", body);
    }

    [Fact]
    public async Task ALineWithNoSku_IsRefused()
    {
        using var desk = OrderCalls.ThroughTheGateway(warehouse);

        using var refused = await OrderCalls.DraftAsync(desk, ("", 2));

        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, body);
        FieldsIn(body).Should().ContainSingle().Which.Should().StartWith("lines[0]", body);
    }

    /// <summary>
    ///     What <c>[ValidateElements(StopOnFirstError = true)]</c> decides: the lines are walked whatever
    ///     the attribute says, and the setting stops the walk at the first line that is wrong.
    /// </summary>
    [Fact]
    public async Task TwoWrongLines_AreAnsweredWithTheFirst()
    {
        using var desk = OrderCalls.ThroughTheGateway(warehouse);

        using var refused = await OrderCalls.DraftAsync(desk, ("SKU-A", 0), ("SKU-B", 0));

        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, body);
        FieldsIn(body).Should().ContainSingle("the walk stops at the first wrong line")
            .Which.Should().StartWith("lines[0]", body);
    }

    [Fact]
    public async Task AShippedOrder_CannotBeCancelled_AndStaysShipped()
    {
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        var id = await OrderCalls.NewDraftAsync(desk);
        await OrderCalls.ShipAsync(warehouse, id);

        using var refused = await desk.PostAsync($"api/orders/{id}/cancel", null);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await OrderCalls.ReadAsync(desk, id)).GetProperty("status").GetString().Should().Be("Shipped");
    }

    /// <summary>The control for the refusal: a draft may be cancelled.</summary>
    [Fact]
    public async Task ADraft_CanBeCancelled()
    {
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        var id = await OrderCalls.NewDraftAsync(desk);

        using var cancelled = await desk.PostAsync($"api/orders/{id}/cancel", null);

        var body = await cancelled.Content.ReadAsStringAsync();
        cancelled.IsSuccessStatusCode.Should().BeTrue(body);
        (await OrderCalls.ReadAsync(desk, id)).GetProperty("status").GetString().Should().Be("Cancelled");

        // The answer is the order as it now is, lines included: a move that answered "no lines" would be
        // telling the caller something the row does not say.
        JsonDocument.Parse(body).RootElement.GetProperty("lines").GetArrayLength().Should().Be(1, body);
    }

    /// <summary>The fields a validation answer names, lower-cased.</summary>
    private static List<string> FieldsIn(string body)
        => [.. JsonDocument.Parse(body).RootElement.GetProperty("errors").EnumerateObject()
            .Select(field => field.Name.ToLowerInvariant())];
}
