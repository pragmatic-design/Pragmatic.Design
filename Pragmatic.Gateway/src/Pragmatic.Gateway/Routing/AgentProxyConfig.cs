using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Pragmatic.Gateway.Routing;

/// <summary>
///     Immutable YARP proxy configuration snapshot produced by <see cref="AgentRouteProvider" />.
/// </summary>
internal sealed class AgentProxyConfig(
    IReadOnlyList<RouteConfig> routes,
    IReadOnlyList<ClusterConfig> clusters,
    IChangeToken changeToken) : IProxyConfig
{
    public IReadOnlyList<RouteConfig> Routes { get; } = routes;
    public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;
    public IChangeToken ChangeToken { get; } = changeToken;
}
