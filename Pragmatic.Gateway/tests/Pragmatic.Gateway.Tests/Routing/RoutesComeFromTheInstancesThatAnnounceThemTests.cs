using System.Text.Json;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Gateway.Routing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Gateway.Tests.Routing;

/// <summary>
///     A route and its cluster are built from the instances that announce them on the Agent KV:
///     one destination per instance, taken in turn.
/// </summary>
public sealed class RoutesComeFromTheInstancesThatAnnounceThemTests
{
    [Fact]
    public void TwoInstancesOfOneRoute_AreOneClusterWithTwoDestinations_TakenInTurn()
    {
        var (routes, clusters) = AnnouncedRoutes.Build(
            [
                Announced("warehouse", "stock-a", "http://127.0.0.1:5222", "/warehouse"),
                Announced("warehouse", "stock-b", "http://127.0.0.1:5223", "/warehouse"),
                Announced("orders", "orders-1", "http://127.0.0.1:5221", "/orders"),
            ],
            (_, ex) => throw ex);

        routes.Select(r => r.RouteId).Should().BeEquivalentTo("warehouse", "orders");
        var warehouse = clusters.Single(c => c.ClusterId == "warehouse");
        warehouse.Destinations!.Values.Select(d => d.Address)
            .Should().BeEquivalentTo(["http://127.0.0.1:5222", "http://127.0.0.1:5223"]);
        warehouse.LoadBalancingPolicy.Should().Be("RoundRobin");
    }

    [Fact]
    public void TheRoute_IsWhatItsInstancesAnnounced()
    {
        var (routes, _) = AnnouncedRoutes.Build(
            [Announced("warehouse", "stock-a", "http://127.0.0.1:5222", "/warehouse")], (_, ex) => throw ex);

        var route = routes.Single();
        (route.ClusterId, route.Match.Path, route.AuthorizationPolicy).Should()
            .Be(("warehouse", "/warehouse/{**catch-all}", "Default"));
        route.Transforms!.Single()["PathRemovePrefix"].Should().Be("/warehouse");
        route.Metadata![GatewayRouteMetadata.AppId].Should().Be("Stock.Host");
    }

    /// <summary>The control: nothing announced, nothing routed.</summary>
    [Fact]
    public void NothingAnnounced_NoRoute()
    {
        var (routes, clusters) = AnnouncedRoutes.Build([], (_, ex) => throw ex);

        (routes.Count, clusters.Count).Should().Be((0, 0));
    }

    /// <summary>One unreadable announcement is skipped and reported; the others still route.</summary>
    [Fact]
    public void AMalformedAnnouncement_IsSkipped_TheOthersRoute()
    {
        var reported = new List<string>();

        var (routes, _) = AnnouncedRoutes.Build(
            [
                new KvEntryPayload { Key = InstanceAnnouncementKeys.Key("orders", "x"), Value = "{not json", Found = true },
                Announced("warehouse", "stock-a", "http://127.0.0.1:5222", "/warehouse"),
            ],
            (key, _) => reported.Add(key));

        routes.Select(r => r.RouteId).Should().BeEquivalentTo("warehouse");
        reported.Should().BeEquivalentTo(InstanceAnnouncementKeys.Key("orders", "x"));
    }

    /// <summary>An instance out of the rotation is not a destination; the one still in it takes everything.</summary>
    [Fact]
    public void AnInstanceOutOfTheRotation_IsNotADestination()
    {
        var (_, clusters) = AnnouncedRoutes.Build(
            [
                Announced("warehouse", "stock-a", "http://127.0.0.1:5222", "/warehouse"),
                Announced("warehouse", "stock-b", "http://127.0.0.1:5223", "/warehouse", inRotation: false),
            ],
            (_, ex) => throw ex);

        clusters.Single().Destinations!.Values.Select(d => d.Address).Should().Equal(["http://127.0.0.1:5222"]);
    }

    /// <summary>
    ///     With every instance out of the rotation the route stays, with no destination, so the gateway answers
    ///     it with the service's maintenance status rather than a 404.
    /// </summary>
    [Fact]
    public void EveryInstanceOutOfTheRotation_TheRouteStays_WithNoDestination()
    {
        var (routes, clusters) = AnnouncedRoutes.Build(
            [
                Announced("warehouse", "stock-a", "http://127.0.0.1:5222", "/warehouse", inRotation: false),
                Announced("warehouse", "stock-b", "http://127.0.0.1:5223", "/warehouse", inRotation: false),
            ],
            (_, ex) => throw ex);

        routes.Single().Metadata![GatewayRouteMetadata.AppId].Should().Be("Stock.Host");
        clusters.Single().Destinations.Should().BeEmpty();
    }

    private static KvEntryPayload Announced(
        string routeId, string instanceId, string address, string prefix, bool inRotation = true) => new()
    {
        Key = InstanceAnnouncementKeys.Key(routeId, instanceId),
        Value = JsonSerializer.Serialize(new InstanceAnnouncementPayload
        {
            Address = address,
            Path = prefix + "/{**catch-all}",
            PathRemovePrefix = prefix,
            RequireAuth = true,
            AppId = routeId == "warehouse" ? "Stock.Host" : "Orders.Host",
            InRotation = inRotation,
        }),
        Found = true,
    };
}
