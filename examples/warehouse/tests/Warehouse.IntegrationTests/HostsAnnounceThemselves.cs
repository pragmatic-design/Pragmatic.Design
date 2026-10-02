using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Gateway;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;

namespace Warehouse.IntegrationTests;

/// <summary>
///     The gateway's routes come from the Agents: each host announces the route it serves and
///     its address to the Agent beside it, the Agents gossip it, and the gateway's Agent tells the gateway.
///     An instance that stops leaves the rotation.
/// </summary>
/// <remarks>
///     <para>
///         The announcement is ephemeral: the Agent deletes it when the host's connection closes, and the
///         delete reaches the gateway's Agent the way the announcement did. Nothing removes a stopped
///         instance by hand.
///     </para>
///     <para>
///         <see cref="TheGatewayIsTheOnlyDoor.TwentyAvailabilityReads_AreServedByBothStockInstances" /> is the
///         control: with both instances up, both answer.
///     </para>
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class HostsAnnounceThemselves(WarehouseFixture warehouse)
{
    /// <summary>How long a stopped instance may still be in the rotation: a disconnect, a delete, a gossip round.</summary>
    private static readonly TimeSpan LeavesWithin = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TheGatewayHasNoStaticRoute_AndTheThreeServicesAnswerThroughIt()
    {
        warehouse.Gateway.Services.GetRequiredService<GatewayOptions>().Routes
            .Should().BeEmpty("every route came from an Agent");

        using var orders = OrderCalls.ThroughTheGateway(warehouse);
        using var stock = StockCalls.ThroughTheGateway(warehouse, StockCalls.Manager);
        using var shipping = ShippingCalls.ThroughTheGateway(warehouse);
        foreach (var (client, service) in new[] { (orders, "orders"), (stock, "stock"), (shipping, "shipping") })
        {
            using var response = await client.GetAsync("health");
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{service} answers: {await response.Content.ReadAsStringAsync()}");
        }
    }

    [Fact]
    public async Task StockBStops_TwentyReadsAllAnswer_AllFromA()
    {
        var product = await StockCalls.InStockAsync(warehouse.StockA, 3);
        using var manager = StockCalls.ThroughTheGateway(warehouse, StockCalls.Manager);

        await warehouse.RestartStockBAsync(async () =>
        {
            // Once the rotation holds A alone, every read is A's; before that, one may still reach B's port.
            try
            {
                var reads = await WarehouseWaits.UntilAsync(
                    () => TwentyReadsAsync(manager, product.Id),
                    round => round.All(read => read.Status == HttpStatusCode.OK && read.ServedBy == warehouse.StockA.ServedBy),
                    "twenty reads, all answered by instance A",
                    LeavesWithin);

                reads.Should().HaveCount(20);
            }
            catch (TimeoutException timeout)
            {
                throw new TimeoutException(
                    $"{timeout.Message}{Environment.NewLine}{await warehouse.AnnouncementsByAgentAsync()}", timeout);
            }
        });

        // B is back, announced again: the next test finds the rotation as it was.
        await WarehouseWaits.UntilAsync(
            () => TwentyReadsAsync(manager, product.Id),
            round => round.Any(read => read.ServedBy == warehouse.StockB.ServedBy),
            "instance B, restarted, back in the rotation",
            LeavesWithin);
    }

    private static async Task<IReadOnlyList<(HttpStatusCode Status, string? ServedBy)>> TwentyReadsAsync(
        HttpClient client, Guid productId)
    {
        var reads = new List<(HttpStatusCode, string?)>();
        for (var read = 0; read < 20; read++)
        {
            using var response = await client.GetAsync($"api/levels?productId={productId}");
            reads.Add((response.StatusCode,
                response.Headers.TryGetValues("X-Served-By", out var values) ? values.Single() : null));
        }

        return reads;
    }
}
