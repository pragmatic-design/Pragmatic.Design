using System.Collections.Concurrent;
using System.Diagnostics;
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

        var answers = new ConcurrentQueue<Answer>();
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
        // Each failure with its body and duration, and the services' own errors: the one time this failed
        // in CI it said only "(place, ServiceUnavailable)", which is not enough to tell a drain defect from
        // a stalled runner (#101).
        answers.Where(answer => (int)answer.Status >= 500).Should().BeEmpty(
            "a drain leaves the rotation before the instance stops taking requests, so nothing is refused. "
            + $"Slowest place: {SlowestOf(answers, "place")}. Orders errors: {warehouse.Orders.Errors}. "
            + $"Stock A errors: {warehouse.StockA.Errors}. Stock B errors: {warehouse.StockB.Errors}");

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

    /// <summary>One call of the traffic: what it was, what came back, and how long it took.</summary>
    private sealed record Answer(string Call, HttpStatusCode Status, TimeSpan Elapsed, string? Body)
    {
        public override string ToString() => $"{Call} {(int)Status} after {Elapsed.TotalMilliseconds:F0} ms: {Body}";
    }

    /// <summary>Availability reads through the gateway, and orders placed through it — each placing reserves stock — until stopped.</summary>
    private static async Task SteadyTrafficAsync(
        HttpClient manager, HttpClient desk, (Guid Id, string Sku) product,
        ConcurrentQueue<Answer> answers, CancellationToken stop)
    {
        var round = 0;
        while (!stop.IsCancellationRequested)
        {
            var clock = Stopwatch.StartNew();
            using (var read = await manager.GetAsync($"api/levels?productId={product.Id}", CancellationToken.None))
                answers.Enqueue(await AnswerAsync("read", read, clock));

            if (round++ % 5 == 0)
            {
                // Placing is the reservation: Orders asks Stock, over the broker, to hold the quantity.
                clock.Restart();
                using var drafted = await OrderCalls.DraftAsync(desk, (product.Sku, 1));
                answers.Enqueue(await AnswerAsync("draft", drafted, clock));
                if (drafted.IsSuccessStatusCode)
                {
                    var order = System.Text.Json.JsonDocument.Parse(await drafted.Content.ReadAsStringAsync())
                        .RootElement.GetProperty("id").GetGuid();
                    clock.Restart();
                    using var placed = await desk.PostAsync($"api/orders/{order}/place", null, CancellationToken.None);
                    answers.Enqueue(await AnswerAsync("place", placed, clock));
                }
            }
        }
    }

    private static string SlowestOf(IEnumerable<Answer> answers, string call)
        => answers.Where(answer => answer.Call == call).MaxBy(answer => answer.Elapsed)?.ToString() ?? "none";

    /// <summary>The answer, with its body when it is a failure: a 5xx is only diagnosable by what it said.</summary>
    private static async Task<Answer> AnswerAsync(string call, HttpResponseMessage response, Stopwatch clock)
    {
        var elapsed = clock.Elapsed;
        var body = (int)response.StatusCode >= 500 ? await response.Content.ReadAsStringAsync() : null;
        return new Answer(call, response.StatusCode, elapsed, body);
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
