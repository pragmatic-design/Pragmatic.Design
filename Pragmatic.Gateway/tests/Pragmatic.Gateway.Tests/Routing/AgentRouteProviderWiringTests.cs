using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Gateway.Routing;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace Pragmatic.Gateway.Tests.Routing;

/// <summary>
///     YARP consumes the <see cref="IProxyConfigProvider" /> registered in DI. Registering
///     <see cref="AgentRouteProvider" /> only as its concrete type would make YARP fall back to an
///     empty in-memory provider and serve no route at all. These boot the real
///     <c>Program</c> and assert the provider YARP resolves IS the Agent-backed one — and that a route
///     configured through it actually reaches the proxy config. Pure wiring assertions guard against the
///     registration silently regressing.
/// </summary>
public sealed class AgentRouteProviderWiringTests(GatewayAppFactory factory)
    : IClassFixture<GatewayAppFactory>
{
    [Fact]
    public void ProxyConfigProvider_ResolvesTo_TheAgentRouteProvider()
    {
        var proxyProvider = factory.Services.GetRequiredService<IProxyConfigProvider>();
        var agentProvider = factory.Services.GetRequiredService<AgentRouteProvider>();

        // The exact guarantee BUG-G1 restores: YARP's provider and the Agent-backed one are one instance.
        proxyProvider.Should().BeSameAs(agentProvider);
    }

    [Fact]
    public void ConfiguredStaticRoute_FlowsThrough_TheProxyConfigProvider()
    {
        var proxyProvider = factory.Services.GetRequiredService<IProxyConfigProvider>();

        var config = proxyProvider.GetConfig();

        // A route present here proves the provider YARP reads is actually populated (not the empty stub).
        config.Routes.Should().Contain(r => r.RouteId == "api");
        config.Clusters.Should().Contain(c => c.ClusterId == "api");
    }
}
