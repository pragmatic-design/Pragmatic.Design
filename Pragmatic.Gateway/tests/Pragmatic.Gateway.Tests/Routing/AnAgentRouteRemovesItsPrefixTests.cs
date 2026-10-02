using Pragmatic.Gateway.Routing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Gateway.Tests.Routing;

/// <summary>
///     The prefix removal, for the routes that come from the Agent's KV: the same declaration as a static route,
///     turned into the same transform.
/// </summary>
/// <remarks>
///     Through <see cref="AgentRouteProvider.ToRouteConfig" /> rather than a KV read, because the gateway
///     suite has no Agent daemon to connect to. The static half is asserted end to end in
///     <see cref="ARouteRemovesItsPrefixTests" />, and both halves build the transform in one place.
/// </remarks>
public sealed class AnAgentRouteRemovesItsPrefixTests
{
    [Fact]
    public void WithPathRemovePrefix_TheRouteCarriesTheTransform()
    {
        var route = AgentRouteProvider.ToRouteConfig("orders", new AgentRouteProvider.RouteDefinition
        {
            Path = "/orders/{**catch-all}",
            PathRemovePrefix = "/orders",
        });

        route.Transforms.Should().ContainSingle();
        route.Transforms![0]["PathRemovePrefix"].Should().Be("/orders");
    }

    /// <summary>The control: no prefix, no transform.</summary>
    [Fact]
    public void WithoutPathRemovePrefix_TheRouteHasNoTransform()
    {
        var route = AgentRouteProvider.ToRouteConfig("orders", new AgentRouteProvider.RouteDefinition
        {
            Path = "/orders/{**catch-all}",
        });

        route.Transforms.Should().BeNull();
    }

    /// <summary>A prefix the path does not start with is refused, so the reload logs and skips the key.</summary>
    [Fact]
    public void APrefixThePathDoesNotStartWith_IsRefused_NamingTheRoute()
    {
        var build = () => AgentRouteProvider.ToRouteConfig("orders", new AgentRouteProvider.RouteDefinition
        {
            Path = "/orders/{**catch-all}",
            PathRemovePrefix = "/shipping",
        });

        build.Should().Throw<InvalidOperationException>().WithMessage("*orders*/shipping*");
    }
}
