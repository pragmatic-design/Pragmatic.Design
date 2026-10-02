using System.Collections.Concurrent;
using System.Net;
using Pragmatic.ControlPlane;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;

namespace Warehouse.IntegrationTests;

/// <summary>
///     A release without a failed request: one Stock instance is drained while the other carries
///     the traffic, and put back afterwards.
/// </summary>
/// <remarks>
///     <para>
///         A drain takes the instance out of the gateway's rotation and leaves it running (owner decision): it
///         reports Draining, the gateway stops sending it new requests, what it has in flight completes, it
///         reports Drained. <c>ExitMaintenanceCommand</c> puts it back.
///     </para>
///     <para>
///         The command is addressed to the instance, through the gateway's Agent: the other instance of the
///         same service is not touched.
///     </para>
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class DrainingOneInstanceFailsNoRequest(WarehouseFixture warehouse)
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task DrainingStockB_UnderSteadyTraffic_FailsNoRequest_AndAServesEverything_UntilBIsPutBack()
    {
        var product = await StockCalls.InStockAsync(warehouse.StockA, quantity: 500);
        using var manager = StockCalls.ThroughTheGateway(warehouse, StockCalls.Manager);
        using var desk = OrderCalls.ThroughTheGateway(warehouse);

        var answers = new ConcurrentQueue<(string Call, HttpStatusCode Status)>();
        using var stop = new CancellationTokenSource();
        var traffic = SteadyTrafficAsync(manager, desk, product, answers, stop.Token);

        try
        {
            await warehouse.SendCommandAsync(warehouse.StockB.Services, new DrainCommand(GracePeriod));
            await warehouse.UntilInStateAsync(warehouse.StockB.Services, HostState.Drained);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        finally
        {
            await stop.CancelAsync();
            await traffic;
        }

        answers.Should().NotBeEmpty();
        answers.Where(answer => (int)answer.Status >= 500).Should().BeEmpty(
            "a drain leaves the rotation before the instance stops taking requests, so nothing is refused");

        // Drained: every read is A's.
        var reads = await ServedByAsync(manager, product.Id, 20);
        reads.Should().OnlyContain(servedBy => servedBy == warehouse.StockA.ServedBy);

        // Put back: B is in the rotation again.
        await warehouse.SendCommandAsync(warehouse.StockB.Services, new ExitMaintenanceCommand());
        await WarehouseWaits.UntilAsync(
            () => ServedByAsync(manager, product.Id, 20),
            round => round.Contains(warehouse.StockB.ServedBy),
            "instance B, put back, in the rotation again");
    }

    /// <summary>
    ///     The control: with both instances drained the service answers its maintenance status, and another
    ///     service still answers.
    /// </summary>
    [Fact]
    public async Task BothStockInstancesDrained_TheWarehouseRouteAnswersMaintenance_OrdersStillAnswers()
    {
        using var manager = StockCalls.ThroughTheGateway(warehouse, StockCalls.Manager);
        using var desk = OrderCalls.ThroughTheGateway(warehouse);

        try
        {
            await warehouse.SendCommandAsync(warehouse.StockA.Services, new DrainCommand(GracePeriod));
            await warehouse.SendCommandAsync(warehouse.StockB.Services, new DrainCommand(GracePeriod));
            await warehouse.UntilInStateAsync(warehouse.StockA.Services, HostState.Drained);
            await warehouse.UntilInStateAsync(warehouse.StockB.Services, HostState.Drained);

            var stock = await WarehouseWaits.UntilAsync(
                async () => (await manager.GetAsync("health")).StatusCode,
                status => status == HttpStatusCode.ServiceUnavailable,
                "the gateway answers the warehouse route with its maintenance status");
            stock.Should().Be(HttpStatusCode.ServiceUnavailable);

            using var orders = await desk.GetAsync("health");
            orders.StatusCode.Should().Be(HttpStatusCode.OK, "only Stock is drained");
        }
        finally
        {
            // Back as it was for the tests that share the fixture.
            await warehouse.SendCommandAsync(warehouse.StockA.Services, new ExitMaintenanceCommand());
            await warehouse.SendCommandAsync(warehouse.StockB.Services, new ExitMaintenanceCommand());
            await warehouse.UntilInStateAsync(warehouse.StockA.Services, HostState.Ready);
            await warehouse.UntilInStateAsync(warehouse.StockB.Services, HostState.Ready);
            await WarehouseWaits.UntilAsync(
                () => ServedByAsync(manager, Guid.Empty, 20),
                round => round.Contains(warehouse.StockA.ServedBy) && round.Contains(warehouse.StockB.ServedBy),
                "both Stock instances back in the rotation");
        }
    }

    /// <summary>Availability reads through the gateway, and orders placed through it — each placing reserves stock — until stopped.</summary>
    private static async Task SteadyTrafficAsync(
        HttpClient manager, HttpClient desk, (Guid Id, string Sku) product,
        ConcurrentQueue<(string Call, HttpStatusCode Status)> answers, CancellationToken stop)
    {
        var round = 0;
        while (!stop.IsCancellationRequested)
        {
            using (var read = await manager.GetAsync($"api/levels?productId={product.Id}", CancellationToken.None))
                answers.Enqueue(("read", read.StatusCode));

            if (round++ % 5 == 0)
            {
                // Placing is the reservation: Orders asks Stock, over the broker, to hold the quantity.
                using var drafted = await OrderCalls.DraftAsync(desk, (product.Sku, 1));
                answers.Enqueue(("draft", drafted.StatusCode));
                if (drafted.IsSuccessStatusCode)
                {
                    var order = System.Text.Json.JsonDocument.Parse(await drafted.Content.ReadAsStringAsync())
                        .RootElement.GetProperty("id").GetGuid();
                    using var placed = await desk.PostAsync($"api/orders/{order}/place", null, CancellationToken.None);
                    answers.Enqueue(("place", placed.StatusCode));
                }
            }
        }
    }

    private static async Task<IReadOnlyList<string?>> ServedByAsync(HttpClient client, Guid productId, int count)
    {
        var servedBy = new List<string?>();
        for (var i = 0; i < count; i++)
        {
            using var response = await client.GetAsync(productId == Guid.Empty ? "health" : $"api/levels?productId={productId}");
            servedBy.Add(response.Headers.TryGetValues("X-Served-By", out var values) ? values.Single() : null);
        }

        return servedBy;
    }
}
