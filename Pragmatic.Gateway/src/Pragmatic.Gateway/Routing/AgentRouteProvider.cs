using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Primitives;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.Protocol.Payloads;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.LoadBalancing;

namespace Pragmatic.Gateway.Routing;

/// <summary>
///     YARP route/cluster provider backed by Agent KV + static file fallback.
///     Agent KV is primary (push-based hot reload). Static routes from GatewayOptions are fallback.
/// </summary>
internal sealed partial class AgentRouteProvider : IProxyConfigProvider, IDisposable
{
    private readonly AgentConnection _agent;
    private readonly GatewayOptions _options;
    private readonly ILogger<AgentRouteProvider> _logger;
    private readonly Lock _reloadLock = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private volatile AgentProxyConfig _config;
    private CancellationTokenSource _reloadCts = new();

    /// <summary>Current loaded route count.</summary>
    public int RouteCount => _config.Routes.Count;

    public AgentRouteProvider(AgentConnection agent, GatewayOptions options, ILogger<AgentRouteProvider> logger)
    {
        _agent = agent;
        _options = options;
        _logger = logger;
        _config = new AgentProxyConfig([], [], new CancellationChangeToken(_reloadCts.Token));
        _agent.OnKvChanged += OnKvChanged;
    }

    public IProxyConfig GetConfig() => _config;

    /// <summary>Forces a reload from Agent KV + file fallback.</summary>
    /// <remarks>
    ///     One at a time, in the order they were asked for. Each change on the KV starts a reload, and each
    ///     reads the KV when it runs; two in flight together would install whichever finished last, and a
    ///     reload that read the KV before an instance announced itself could replace the one that saw it —
    ///     leaving the instance out of the rotation until the next change.
    /// </remarks>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _reloadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ReloadOnceAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task ReloadOnceAsync(CancellationToken ct)
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        // 1. Try Agent KV (dynamic routes)
        if (_agent.IsConnected)
        {
            var kvRoutes = await LoadRoutesFromAgentAsync(ct).ConfigureAwait(false);
            var kvClusters = await LoadClustersFromAgentAsync(ct).ConfigureAwait(false);
            routes.AddRange(kvRoutes);
            clusters.AddRange(kvClusters);

            // 1b. Routes the running instances announced. A route an operator wrote above wins.
            var announcements = await _agent.KvPrefixAsync(InstanceAnnouncementKeys.Prefix, ct).ConfigureAwait(false);
            var (announcedRoutes, announcedClusters) = AnnouncedRoutes.Build(announcements, LogAnnouncementUnreadable);
            foreach (var route in announcedRoutes)
            {
                if (routes.Exists(r => r.RouteId == route.RouteId))
                    continue;
                routes.Add(route);
                clusters.Add(announcedClusters.Single(c => c.ClusterId == route.ClusterId));
            }
        }

        // 2. Add static routes from config (as fallback / base routes)
        foreach (var entry in _options.Routes)
        {
            // Don't duplicate if Agent already provided this route
            if (routes.Exists(r => r.RouteId == entry.RouteId))
                continue;

            // Refused rather than skipped: YARP would reject the cluster anyway, with a message that
            // names a destination this class invented and not the route the operator wrote.
            if (entry.Backends.Count == 0)
                throw new InvalidOperationException(
                    $"Gateway route '{entry.RouteId}' names no backend. Set Gateway:Routes:<n>:Backends "
                    + "to the address of each instance of the service it fronts.");

            routes.Add(new RouteConfig
            {
                RouteId = entry.RouteId,
                ClusterId = entry.RouteId,
                // RequireAuth wires the route to the default authorization policy (authenticated user).
                // Without this the flag was silently ignored and every route was an anonymous passthrough.
                AuthorizationPolicy = entry.RequireAuth ? "Default" : null,
                // Per-route rate limit override (RouteEntry.RateLimit, requests/minute) — the global
                // limiter reads this from the matched route and caps this route tighter than the default.
                Metadata = entry.RateLimit is { } rl
                    ? new Dictionary<string, string> { [GatewayRouteMetadata.RateLimit] = rl.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                    : null,
                Match = new RouteMatch
                {
                    Path = entry.Path,
                    Hosts = entry.Host is not null ? [entry.Host] : null
                },
                Transforms = PrefixTransforms(entry.RouteId, entry.Path, entry.PathRemovePrefix)
            });

            var destinations = new Dictionary<string, DestinationConfig>();
            for (var i = 0; i < entry.Backends.Count; i++)
                destinations[$"d{i}"] = new DestinationConfig { Address = entry.Backends[i] };

            clusters.Add(new ClusterConfig
            {
                ClusterId = entry.RouteId,
                // In turn, not YARP's default PowerOfTwoChoices: with no requests in flight that picks at
                // random, so two instances of one service would share the load only on average.
                LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
                Destinations = destinations
            });
        }

        // Atomically swap the CTS and config so concurrent calls cannot cancel/dispose
        // a CancellationTokenSource that is still being observed by in-flight requests.
        CancellationTokenSource oldCts;
        lock (_reloadLock)
        {
            oldCts = _reloadCts;
            _reloadCts = new CancellationTokenSource();
            _config = new AgentProxyConfig(routes, clusters, new CancellationChangeToken(_reloadCts.Token));
        }

        oldCts.Cancel();
        oldCts.Dispose();
    }

    public void Dispose()
    {
        _agent.OnKvChanged -= OnKvChanged;
        _reloadCts.Dispose();
        _reloadGate.Dispose();
    }

    private void OnKvChanged(KvChangedPayload change)
    {
        if (!change.Key.StartsWith(GatewayKvSchema.Prefix, StringComparison.Ordinal))
            return;

        // Run reload on a background task and log any exception so it is observable.
        _ = Task.Run(async () =>
        {
            try
            {
                await ReloadAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogReloadFailed(ex);
            }
        });
    }

    private async Task<List<RouteConfig>> LoadRoutesFromAgentAsync(CancellationToken ct)
    {
        var entries = await _agent.KvPrefixAsync(GatewayKvSchema.RoutesPrefix, ct).ConfigureAwait(false);
        var routes = new List<RouteConfig>();

        foreach (var entry in entries)
        {
            if (entry.Value is null) continue;
            try
            {
                var def = JsonSerializer.Deserialize(entry.Value, RouteJsonContext.Default.RouteDefinition);
                if (def is null) continue;
                var routeId = entry.Key[GatewayKvSchema.RoutesPrefix.Length..];
                routes.Add(ToRouteConfig(routeId, def));
            }
            catch (Exception ex)
            {
                LogRouteDeserializationFailed(entry.Key, ex);
            }
        }

        return routes;
    }

    private async Task<List<ClusterConfig>> LoadClustersFromAgentAsync(CancellationToken ct)
    {
        var entries = await _agent.KvPrefixAsync(GatewayKvSchema.ClustersPrefix, ct).ConfigureAwait(false);
        var clusters = new List<ClusterConfig>();

        foreach (var entry in entries)
        {
            if (entry.Value is null) continue;
            try
            {
                var def = JsonSerializer.Deserialize(entry.Value, RouteJsonContext.Default.ClusterDefinition);
                if (def is null) continue;
                var clusterId = entry.Key[GatewayKvSchema.ClustersPrefix.Length..];
                var destinations = new Dictionary<string, DestinationConfig>();
                if (def.Destinations is not null)
                    foreach (var dest in def.Destinations)
                        destinations[dest.Key] = new DestinationConfig { Address = dest.Value };
                clusters.Add(new ClusterConfig { ClusterId = clusterId, Destinations = destinations });
            }
            catch (Exception ex)
            {
                LogClusterDeserializationFailed(entry.Key, ex);
            }
        }

        return clusters;
    }

    /// <summary>The YARP route a route definition read from the Agent's KV describes.</summary>
    /// <remarks>
    ///     A definition whose prefix its path does not start with throws, and the caller logs it and skips
    ///     that route: a KV reload happens while the gateway is serving, and one bad key must not take the
    ///     other routes with it. The static routes fail the start instead, because nothing is served yet.
    /// </remarks>
    internal static RouteConfig ToRouteConfig(string routeId, RouteDefinition def) => new()
    {
        RouteId = routeId,
        ClusterId = def.ClusterId ?? routeId,
        AuthorizationPolicy = def.RequireAuth ? "Default" : null,
        // Carry the target app id in route metadata so the proxy-pipeline maintenance check
        // (GW-M3) can 503 only requests routed to an app that is in maintenance.
        Metadata = def.AppId is null
            ? null
            : new Dictionary<string, string> { [GatewayRouteMetadata.AppId] = def.AppId },
        Match = new RouteMatch { Path = def.Path, Hosts = def.Hosts },
        Transforms = PrefixTransforms(routeId, def.Path, def.PathRemovePrefix)
    };

    /// <summary>YARP's <c>PathRemovePrefix</c> transform for a route that declares one, or none.</summary>
    private static IReadOnlyList<IReadOnlyDictionary<string, string>>? PrefixTransforms(
        string routeId, string? path, string? prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return null;

        // Compared as the route matcher compares: case-insensitively.
        if (path is null || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Gateway route '{routeId}' removes the prefix '{prefix}', but its path '{path}' does not "
                + "start with it, so nothing would ever be removed.");

        return [new Dictionary<string, string> { ["PathRemovePrefix"] = prefix }];
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to reload routes/clusters from Agent KV")]
    private partial void LogReloadFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping malformed route definition at key '{Key}'")]
    private partial void LogRouteDeserializationFailed(string key, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping malformed cluster definition at key '{Key}'")]
    private partial void LogClusterDeserializationFailed(string key, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping an instance announcement that cannot be routed, at key '{Key}'")]
    private partial void LogAnnouncementUnreadable(string key, Exception ex);

    internal sealed class RouteDefinition
    {
        public string? Path { get; set; }

        /// <summary>The prefix removed before forwarding — the same as <c>RouteEntry.PathRemovePrefix</c>.</summary>
        public string? PathRemovePrefix { get; set; }
        public IReadOnlyList<string>? Hosts { get; set; }
        public string? ClusterId { get; set; }
        public bool RequireAuth { get; set; }

        /// <summary>The roster app id this route targets (<c>state/app:{AppId}/…</c>) — enables per-app
        /// maintenance (GW-M3): only requests routed to an app with every instance in maintenance get 503.</summary>
        public string? AppId { get; set; }
    }

    private sealed class ClusterDefinition
    {
        public Dictionary<string, string>? Destinations { get; set; }
    }

    // Nested so the source-generated context can see the private route/cluster DTOs.
    // Default naming (no policy) preserves the original reflection-based deserialization behavior.
    [JsonSerializable(typeof(RouteDefinition))]
    [JsonSerializable(typeof(ClusterDefinition))]
    private sealed partial class RouteJsonContext : JsonSerializerContext;
}
