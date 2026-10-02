using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Agent.Protocol.Payloads;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.LoadBalancing;

namespace Pragmatic.Gateway.Routing;

/// <summary>
///     The routes, and their clusters, that running instances announced on the Agent KV
///     (<see cref="InstanceAnnouncementKeys" />): one route per route id, one destination per instance.
/// </summary>
/// <remarks>
///     <para>
///         The instances are the source of truth, and there is no route key of its own to go stale: when
///         the last instance of a route leaves, so does the route. An instance that drains stays announced
///         and out of the rotation (<see cref="InstanceAnnouncementPayload.InRotation" />): the route keeps
///         existing, with fewer destinations or none.
///     </para>
///     <para>
///         The route is read from the first instance by id, so the choice does not depend on the order the
///         KV lists them in. Its instances announce the same route, being the same service.
///     </para>
/// </remarks>
internal static partial class AnnouncedRoutes
{
    public static (List<RouteConfig> Routes, List<ClusterConfig> Clusters) Build(
        IReadOnlyList<KvEntryPayload> entries, Action<string, Exception> onMalformed)
    {
        var byRoute = new SortedDictionary<string, SortedDictionary<string, InstanceAnnouncementPayload>>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            try
            {
                var (routeId, instanceId) = Split(entry.Key);
                var announced = JsonSerializer.Deserialize(entry.Value ?? "", AnnouncementJsonContext.Default.InstanceAnnouncementPayload)
                    ?? throw new JsonException("The announcement is empty.");

                if (!byRoute.TryGetValue(routeId, out var instances))
                    byRoute[routeId] = instances = new SortedDictionary<string, InstanceAnnouncementPayload>(StringComparer.Ordinal);
                instances[instanceId] = announced;
            }
            catch (JsonException ex)
            {
                onMalformed(entry.Key, ex);
            }
            catch (FormatException ex)
            {
                onMalformed(entry.Key, ex);
            }
        }

        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();
        foreach (var (routeId, instances) in byRoute)
        {
            var first = instances.Values.First();
            RouteConfig route;
            try
            {
                route = AgentRouteProvider.ToRouteConfig(routeId, new AgentRouteProvider.RouteDefinition
                {
                    Path = first.Path,
                    PathRemovePrefix = first.PathRemovePrefix,
                    RequireAuth = first.RequireAuth,
                    AppId = first.AppId,
                });
            }
            catch (InvalidOperationException ex)
            {
                // A prefix its path does not start with: this route is skipped, the others still route.
                onMalformed(InstanceAnnouncementKeys.Key(routeId, instances.Keys.First()), ex);
                continue;
            }

            routes.Add(route);
            clusters.Add(new ClusterConfig
            {
                ClusterId = routeId,
                // In turn, as a static route's cluster: with no request in flight, the default picks at random.
                LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
                // Only the instances in the rotation. One that drains keeps its announcement, and so
                // the route — with none left, the route still matches and the service's maintenance status
                // answers it, where dropping the route would answer 404.
                Destinations = instances.Where(instance => instance.Value.InRotation).ToDictionary(
                    instance => instance.Key,
                    instance => new DestinationConfig { Address = instance.Value.Address }),
            });
        }

        return (routes, clusters);
    }

    /// <summary><c>gateway/instances/{routeId}/{instanceId}</c> into its two ids.</summary>
    private static (string RouteId, string InstanceId) Split(string key)
    {
        var rest = key.StartsWith(InstanceAnnouncementKeys.Prefix, StringComparison.Ordinal)
            ? key[InstanceAnnouncementKeys.Prefix.Length..]
            : throw new FormatException($"'{key}' is not an instance announcement key.");

        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 && slash < rest.Length - 1
            ? (rest[..slash], rest[(slash + 1)..])
            : throw new FormatException($"'{key}' does not name a route and an instance.");
    }

    [JsonSerializable(typeof(InstanceAnnouncementPayload))]
    private sealed partial class AnnouncementJsonContext : JsonSerializerContext;
}
