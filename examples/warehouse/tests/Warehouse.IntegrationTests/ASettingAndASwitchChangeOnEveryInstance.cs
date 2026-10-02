using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.FeatureFlags;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;
using Warehouse.Stock.Host;
using Warehouse.Stock.Infrastructure.FeatureFlags;

namespace Warehouse.IntegrationTests;

/// <summary>
///     A setting and a switch changed at runtime, through the Agent, and seen by both Stock
///     instances without a restart: the reorder threshold, and whether an order short of stock is
///     accepted with the missing part backordered.
/// </summary>
/// <remarks>
///     <para>
///         Written to the gateway's Agent, not to the Agent beside either Stock instance: the Agents are
///         one cluster, and a change made anywhere reaches them all. Nothing is edited in a file and no
///         host restarts.
///     </para>
///     <para>
///         Each test puts back what it changed: the suite shares one set of Agents.
///     </para>
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class ASettingAndASwitchChangeOnEveryInstance(WarehouseFixture warehouse)
{
    private const string ThresholdKey = "config/Warehouse:DefaultReorderThreshold";
    private static readonly string FlagKey = $"flags/{AcceptBackordersFlag.Name}";

    /// <summary>The control: nothing written, both instances answer the default threshold and the flag off.</summary>
    [Fact]
    public async Task NothingChanged_BothInstancesAnswerTheDefaults()
    {
        foreach (var instance in new[] { warehouse.StockA, warehouse.StockB })
        {
            (await ToReorderAsync(instance)).GetProperty("threshold").GetInt32().Should().Be(5, instance.ServedBy);
            (await BackordersAcceptedByAsync(instance)).Should().BeFalse(instance.ServedBy);
        }
    }

    /// <remarks>
    ///     The product has no threshold of its own, so the default applies. The control has one: a product
    ///     that says 5 stays off the list whatever the default.
    /// </remarks>
    [Fact]
    public async Task TheThresholdRaisedThroughTheAgent_PutsAProductOnBothReorderLists()
    {
        var (product, _) = await StockCalls.InStockAsync(warehouse.StockA, 7, reorderThreshold: 0);
        var (ownThreshold, _) = await StockCalls.InStockAsync(warehouse.StockA, 7, reorderThreshold: 5);
        foreach (var instance in new[] { warehouse.StockA, warehouse.StockB })
            ListsProduct(await ToReorderAsync(instance), product).Should().BeFalse("7 on hand is not below 5");

        await warehouse.WriteThroughTheAgentAsync(ThresholdKey, "10");
        try
        {
            foreach (var instance in new[] { warehouse.StockA, warehouse.StockB })
            {
                var list = await WarehouseWaits.UntilAsync(
                    () => ToReorderAsync(instance),
                    answer => answer.GetProperty("threshold").GetInt32() == 10,
                    $"{instance.ServedBy} reads the threshold written through the Agent");

                ListsProduct(list, product).Should().BeTrue($"7 on hand is below 10, on {instance.ServedBy}");
                ListsProduct(list, ownThreshold).Should().BeFalse("its own threshold, 5, wins over the default");
            }
        }
        finally
        {
            await warehouse.DeleteThroughTheAgentAsync(ThresholdKey);
        }
    }

    [Fact]
    public async Task BackordersSwitchedOn_AShortOrderIsAccepted_AndOffAgain_It_Is409()
    {
        var (_, sku) = await StockCalls.InStockAsync(warehouse.StockA, 2);
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        var order = await DraftAsync(desk, sku, 5);

        using (var refused = await desk.PostAsync($"api/orders/{order}/place", null))
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict, "the switch is off, so placing an order the stock cannot cover is refused");

        await SwitchAsync(on: true);
        try
        {
            using var accepted = await desk.PostAsync($"api/orders/{order}/place", null);
            accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

            var line = (await OrderCalls.ReadAsync(desk, order)).GetProperty("lines").EnumerateArray().Single();
            (line.GetProperty("quantity").GetInt32(), line.GetProperty("backordered").GetInt32())
                .Should().Be((5, 3), "two were held, three are backordered");
        }
        finally
        {
            await SwitchAsync(on: false);
        }

        var another = await DraftAsync(desk, sku, 5);
        using var refusedAgain = await desk.PostAsync($"api/orders/{another}/place", null);
        refusedAgain.StatusCode.Should().Be(HttpStatusCode.Conflict, "the switch is off again");
    }

    /// <summary>Turns the switch on or off through the Agent, and returns once both instances see it.</summary>
    private async Task SwitchAsync(bool on)
    {
        await warehouse.WriteThroughTheAgentAsync(FlagKey,
            JsonSerializer.Serialize(new { Name = AcceptBackordersFlag.Name, Enabled = on }));

        foreach (var instance in new[] { warehouse.StockA, warehouse.StockB })
            await WarehouseWaits.UntilAsync(() => BackordersAcceptedByAsync(instance), seen => seen == on,
                $"{instance.ServedBy} sees {AcceptBackordersFlag.Name} {(on ? "on" : "off")}");
    }

    private static async Task<bool> BackordersAcceptedByAsync(ServiceHost<StockHost> instance)
    {
        await using var scope = instance.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IFeatureFlags>().IsEnabledAsync<AcceptBackordersFlag>();
    }

    private static async Task<JsonElement> ToReorderAsync(ServiceHost<StockHost> instance)
    {
        using var manager = StockCalls.ToInstance(instance, StockCalls.Manager);
        using var response = await manager.GetAsync("api/products/to-reorder");
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GET api/products/to-reorder answered {(int)response.StatusCode}: {body}");

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static bool ListsProduct(JsonElement list, Guid product)
        => list.GetProperty("products").EnumerateArray().Any(p => p.GetProperty("productId").GetGuid() == product);

    private static async Task<Guid> DraftAsync(HttpClient desk, string sku, int quantity)
    {
        using var created = await OrderCalls.DraftAsync(desk, (sku, quantity));
        var body = await created.Content.ReadAsStringAsync();
        if (!created.IsSuccessStatusCode)
            throw new InvalidOperationException($"POST api/orders answered {(int)created.StatusCode}: {body}");

        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }
}
