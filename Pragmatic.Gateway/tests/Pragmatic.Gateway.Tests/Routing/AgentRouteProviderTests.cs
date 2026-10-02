using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Agent.Client;
using Pragmatic.Gateway.Routing;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace Pragmatic.Gateway.Tests.Routing;

/// <summary>
///     Unit tests for the static-fallback path of <see cref="AgentRouteProvider" />.
///     A freshly constructed <see cref="AgentConnection" /> is never connected
///     (<c>IsConnected == false</c>), so <c>ReloadAsync</c> never touches the socket and
///     only the <see cref="GatewayOptions.Routes" /> fallback is exercised — pure config building.
/// </summary>
public sealed class AgentRouteProviderTests
{
    private static AgentRouteProvider CreateProvider(GatewayOptions options)
    {
        // Non-connected: socket path is irrelevant because ConnectAsync is never called.
        var agent = new AgentConnection("test-agent-not-connected");
        return new AgentRouteProvider(agent, options, NullLogger<AgentRouteProvider>.Instance);
    }

    [Fact]
    public void GetConfig_BeforeReload_ReturnsEmptyConfig()
    {
        var provider = CreateProvider(new GatewayOptions());

        var config = provider.GetConfig();

        config.Routes.Should().BeEmpty();
        config.Clusters.Should().BeEmpty();
        provider.RouteCount.Should().Be(0);
    }

    [Fact]
    public async Task ReloadAsync_WithStaticRoute_BuildsRouteAndCluster()
    {
        var options = new GatewayOptions
        {
            Routes =
            [
                new RouteEntry { RouteId = "api", Path = "/api/{**catch-all}", Backends = ["http://backend:5000"] }
            ]
        };
        var provider = CreateProvider(options);

        await provider.ReloadAsync();

        var config = provider.GetConfig();
        config.Routes.Should().ContainSingle();
        var route = config.Routes[0];
        route.RouteId.Should().Be("api");
        route.ClusterId.Should().Be("api");
        route.Match.Path.Should().Be("/api/{**catch-all}");

        config.Clusters.Should().ContainSingle();
        var cluster = config.Clusters[0];
        cluster.ClusterId.Should().Be("api");
        cluster.Destinations.Should().ContainSingle();
        cluster.Destinations!["d0"].Address.Should().Be("http://backend:5000");
    }

    [Fact]
    public async Task ReloadAsync_WithTwoBackends_BuildsOneClusterWithTwoDestinations()
    {
        var options = new GatewayOptions
        {
            Routes =
            [
                new RouteEntry
                {
                    RouteId = "stock", Path = "/stock/{**catch-all}",
                    Backends = ["http://stock-a:5000", "http://stock-b:5000"]
                }
            ]
        };
        var provider = CreateProvider(options);

        await provider.ReloadAsync();

        var cluster = provider.GetConfig().Clusters.Should().ContainSingle().Subject;
        cluster.Destinations!.Values.Select(d => d.Address).Should().BeEquivalentTo(
            ["http://stock-a:5000", "http://stock-b:5000"]);
        cluster.LoadBalancingPolicy.Should().Be("RoundRobin");
    }

    [Fact]
    public async Task ReloadAsync_WithHost_SetsHostConstraintOnMatch()
    {
        var options = new GatewayOptions
        {
            Routes =
            [
                new RouteEntry { RouteId = "web", Path = "/", Backends = ["http://web:80"], Host = "acme.example.com" }
            ]
        };
        var provider = CreateProvider(options);

        await provider.ReloadAsync();

        var route = provider.GetConfig().Routes[0];
        route.Match.Hosts.Should().BeEquivalentTo("acme.example.com");
    }

    [Fact]
    public async Task ReloadAsync_WithoutHost_LeavesHostsNull()
    {
        var options = new GatewayOptions
        {
            Routes = [new RouteEntry { RouteId = "r", Path = "/r", Backends = ["http://b"] }]
        };
        var provider = CreateProvider(options);

        await provider.ReloadAsync();

        provider.GetConfig().Routes[0].Match.Hosts.Should().BeNull();
    }

    [Fact]
    public async Task ReloadAsync_RouteWithRateLimit_PutsItInRouteMetadata()
    {
        var options = new GatewayOptions
        {
            Routes = [new RouteEntry { RouteId = "api", Path = "/api", Backends = ["http://b"], RateLimit = 120 }]
        };
        var provider = CreateProvider(options);

        await provider.ReloadAsync();

        var route = provider.GetConfig().Routes.Single();
        route.Metadata.Should().NotBeNull();
        route.Metadata!.Should().ContainKey(GatewayRouteMetadata.RateLimit)
            .WhoseValue.Should().Be("120");
    }

    [Fact]
    public async Task ReloadAsync_RouteWithoutRateLimit_HasNoRateLimitMetadata()
    {
        var options = new GatewayOptions
        {
            Routes = [new RouteEntry { RouteId = "api", Path = "/api", Backends = ["http://b"] }]
        };
        var provider = CreateProvider(options);

        await provider.ReloadAsync();

        var route = provider.GetConfig().Routes.Single();
        (route.Metadata?.ContainsKey(GatewayRouteMetadata.RateLimit) ?? false).Should().BeFalse();
    }

    [Fact]
    public async Task ReloadAsync_WithMultipleRoutes_BuildsAllRoutesAndClusters()
    {
        var options = new GatewayOptions
        {
            Routes =
            [
                new RouteEntry { RouteId = "a", Path = "/a", Backends = ["http://a"] },
                new RouteEntry { RouteId = "b", Path = "/b", Backends = ["http://b"] },
                new RouteEntry { RouteId = "c", Path = "/c", Backends = ["http://c"] }
            ]
        };
        var provider = CreateProvider(options);

        await provider.ReloadAsync();

        var config = provider.GetConfig();
        config.Routes.Should().HaveCount(3);
        config.Clusters.Should().HaveCount(3);
        provider.RouteCount.Should().Be(3);
        config.Routes.Select(r => r.RouteId).Should().BeEquivalentTo("a", "b", "c");
    }

    [Fact]
    public async Task ReloadAsync_WithEmptyRoutes_ProducesEmptyConfig()
    {
        var provider = CreateProvider(new GatewayOptions());

        await provider.ReloadAsync();

        var config = provider.GetConfig();
        config.Routes.Should().BeEmpty();
        config.Clusters.Should().BeEmpty();
    }

    [Fact]
    public async Task ReloadAsync_ProducesFreshChangeTokenAndSignalsOldOne()
    {
        var options = new GatewayOptions
        {
            Routes = [new RouteEntry { RouteId = "r", Path = "/r", Backends = ["http://b"] }]
        };
        var provider = CreateProvider(options);

        await provider.ReloadAsync();
        var firstToken = provider.GetConfig().ChangeToken;
        firstToken.HasChanged.Should().BeFalse();

        // A second reload swaps the config and cancels the previous CTS, firing the old token.
        await provider.ReloadAsync();

        firstToken.HasChanged.Should().BeTrue();
        provider.GetConfig().ChangeToken.HasChanged.Should().BeFalse();
    }

    [Fact]
    public async Task ReloadAsync_CalledTwice_ReplacesConfigWithoutDuplicating()
    {
        var options = new GatewayOptions
        {
            Routes = [new RouteEntry { RouteId = "only", Path = "/only", Backends = ["http://b"] }]
        };
        var provider = CreateProvider(options);

        await provider.ReloadAsync();
        await provider.ReloadAsync();

        provider.GetConfig().Routes.Should().ContainSingle();
        provider.RouteCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var provider = CreateProvider(new GatewayOptions());

        var dispose = () => provider.Dispose();

        dispose.Should().NotThrow();
    }
}
