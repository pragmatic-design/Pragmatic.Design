using System.Net;
using System.Net.Http.Json;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;

namespace Warehouse.IntegrationTests;

/// <summary>
///     A level changes only through a movement: a receipt raises it by exactly what arrived, and
///     an adjustment that would leave less than nothing is refused and changes nothing.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>These talk to one Stock instance, not through the gateway.</b> The availability read is
///         <c>[Cacheable]</c> per instance, and the invalidation reaches the other instance over Redis a
///         moment after the save rather than with it: a read through the gateway right after a write can
///         land on the instance that has not dropped its copy yet. What two instances agree on is its own
///         test, <see cref="TwoInstancesAgreeOnAvailability" />.
///     </para>
///     <para>
///         The catalogue's own tests — the SKU, the name — carry no cache and go through the gateway: see
///         <see cref="TheCatalogueThroughTheGateway" />.
///     </para>
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class TheStockChangesOnlyByAMovement(WarehouseFixture warehouse)
{
    [Fact]
    public async Task ReceivingGoods_RaisesTheLevel_ByExactlyWhatArrived()
    {
        using var manager = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        var (product, _) = await StockCalls.NewProductAsync(manager);
        var location = await StockCalls.NewLocationAsync(manager);

        (await StockCalls.LevelsOfAsync(manager, product)).Should().BeEmpty("nothing was ever received");

        (await Receive(manager, product, location, 7)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await Receive(manager, product, location, 5)).StatusCode.Should().Be(HttpStatusCode.Created);

        var level = (await StockCalls.LevelsOfAsync(manager, product)).Should().ContainSingle().Subject;
        level.GetProperty("onHand").GetInt32().Should().Be(12);
        level.GetProperty("available").GetInt32().Should().Be(12, "nothing is reserved yet");
    }

    [Fact]
    public async Task AnAdjustmentBelowZero_IsRefused_AndTheLevelIsUnchanged()
    {
        using var manager = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        var (product, _) = await StockCalls.NewProductAsync(manager);
        var location = await StockCalls.NewLocationAsync(manager);
        await Receive(manager, product, location, 3);

        using var refused = await manager.PostAsJsonAsync("api/levels/adjustments",
            new { productId = product, locationId = location, delta = -5, reason = "Count found two" });

        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain("STOCK_WOULD_GO_NEGATIVE");

        var level = (await StockCalls.LevelsOfAsync(manager, product)).Should().ContainSingle().Subject;
        level.GetProperty("onHand").GetInt32().Should().Be(3, "a refused adjustment moves nothing");
    }

    /// <summary>The control for the refusal: an adjustment that stays at or above zero is applied.</summary>
    [Fact]
    public async Task AnAdjustmentThatLeavesStock_IsApplied()
    {
        using var manager = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        var (product, _) = await StockCalls.NewProductAsync(manager);
        var location = await StockCalls.NewLocationAsync(manager);
        await Receive(manager, product, location, 3);

        using var applied = await manager.PostAsJsonAsync("api/levels/adjustments",
            new { productId = product, locationId = location, delta = -3, reason = "Damaged in the bay" });

        applied.StatusCode.Should().Be(HttpStatusCode.Created, await applied.Content.ReadAsStringAsync());
        (await StockCalls.LevelsOfAsync(manager, product)).Single().GetProperty("onHand").GetInt32().Should().Be(0);
    }

    /// <summary>Correcting a level is the manager's: a clerk who may receive goods may not adjust them.</summary>
    [Fact]
    public async Task AClerk_MayReceive_ButNotAdjust()
    {
        using var manager = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        using var clerk = StockCalls.ToInstance(warehouse.StockA, StockCalls.Clerk);
        var (product, _) = await StockCalls.NewProductAsync(manager);
        var location = await StockCalls.NewLocationAsync(manager);

        (await Receive(clerk, product, location, 2)).StatusCode.Should().Be(HttpStatusCode.Created);

        using var adjusted = await clerk.PostAsJsonAsync("api/levels/adjustments",
            new { productId = product, locationId = location, delta = 1, reason = "Found one" });
        adjusted.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    ///     The control for the cache: without a movement the read answers the same numbers, and a movement
    ///     changes them — the receipt drops what the read cached.
    /// </summary>
    [Fact]
    public async Task TheSameReadTwice_AnswersTheSame_UntilAMovementChangesIt()
    {
        using var manager = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        var (product, _) = await StockCalls.NewProductAsync(manager);
        var location = await StockCalls.NewLocationAsync(manager);
        await Receive(manager, product, location, 4);

        var first = (await StockCalls.LevelsOfAsync(manager, product)).Single().GetProperty("onHand").GetInt32();
        var second = (await StockCalls.LevelsOfAsync(manager, product)).Single().GetProperty("onHand").GetInt32();
        second.Should().Be(first);

        await Receive(manager, product, location, 6);

        (await StockCalls.LevelsOfAsync(manager, product)).Single().GetProperty("onHand").GetInt32()
            .Should().Be(10, "the receipt invalidated the cached answer");
    }

    [Fact]
    public async Task ReceivingAtALocationThatDoesNotExist_Is404()
    {
        using var manager = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        var (product, _) = await StockCalls.NewProductAsync(manager);

        using var response = await manager.PostAsJsonAsync("api/levels/receipts",
            new { productId = product, locationId = Guid.NewGuid(), quantity = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReceivingNothing_IsRefused_BeforeAnythingIsWritten()
    {
        using var manager = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        var (product, _) = await StockCalls.NewProductAsync(manager);
        var location = await StockCalls.NewLocationAsync(manager);

        using var response = await Receive(manager, product, location, 0);

        ((int)response.StatusCode).Should().BeInRange(400, 422);
        (await StockCalls.LevelsOfAsync(manager, product)).Should().BeEmpty();
    }

    private static Task<HttpResponseMessage> Receive(HttpClient client, Guid product, Guid location, int quantity)
        => client.PostAsJsonAsync("api/levels/receipts",
            new { productId = product, locationId = location, quantity });
}
