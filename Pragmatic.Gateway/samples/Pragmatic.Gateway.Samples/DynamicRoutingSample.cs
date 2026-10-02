using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Pragmatic.Gateway.Samples;

/// <summary>
///     Demonstrates how the Gateway turns its routing model into YARP <see cref="RouteConfig" /> /
///     <see cref="ClusterConfig" /> objects, and how hot reload works via an
///     <see cref="IProxyConfig" /> change token.
///     <para>
///         The production <c>AgentRouteProvider</c> is internal and pulls dynamic routes from the
///         Agent KV store (push-based hot reload), then merges the static <see cref="RouteEntry" />
///         list from <see cref="GatewayOptions" /> as a fallback. This sample reproduces that
///         static-fallback projection — the exact mapping the provider performs — and a minimal
///         <see cref="IProxyConfig" /> snapshot with a signalable change token, so the mechanics are
///         runnable without a live Agent or YARP host.
///     </para>
/// </summary>
internal static class DynamicRoutingSample
{
    public static void Run()
    {
        SampleConsole.Header("Dynamic routing — RouteConfig/ClusterConfig + hot reload");

        var options = new GatewayOptions
        {
            Routes =
            [
                new RouteEntry { RouteId = "api", Path = "/api/{**catch-all}", Backends = ["http://api:5000"], RequireAuth = true },
                new RouteEntry { RouteId = "web", Path = "/{**catch-all}", Backends = ["http://web:3000"], Host = "acme.example.com" }
            ]
        };

        // Project the static RouteEntry fallback into YARP config (same shape AgentRouteProvider builds).
        var (routes, clusters) = BuildFromStaticRoutes(options);

        SampleConsole.Section("Projected YARP routes");
        foreach (var r in routes)
            SampleConsole.Item(r.RouteId, $"path='{r.Match.Path}' host={Format(r.Match.Hosts)} -> cluster '{r.ClusterId}'");

        SampleConsole.Section("Projected YARP clusters");
        foreach (var c in clusters)
        {
            var dest = c.Destinations!.First();
            SampleConsole.Item(c.ClusterId, $"{dest.Key} = {dest.Value.Address}");
        }

        // ── Hot reload via IProxyConfig change token ──────────────────────────────────────────
        SampleConsole.Section("Hot reload (IProxyConfig change token)");
        var v1 = new SampleProxyConfig(routes, clusters);
        SampleConsole.Item("snapshot v1 routes", v1.Routes.Count);
        SampleConsole.Item("v1 token signalled?", v1.ChangeToken.HasChanged);

        // A new route arrives from the Agent KV (e.g. gateway/routes/orders). Build a fresh snapshot
        // and signal the previous token — exactly how YARP is told to pick up the new config.
        var newRoutes = routes.Append(new RouteConfig
        {
            RouteId = "orders",
            ClusterId = "orders",
            Match = new RouteMatch { Path = "/orders/{**catch-all}" }
        }).ToList();

        var v2 = new SampleProxyConfig(newRoutes, clusters);
        v1.SignalChange(); // provider swaps _config then cancels the old CTS

        SampleConsole.Item("v1 token signalled?", v1.ChangeToken.HasChanged);
        SampleConsole.Item("snapshot v2 routes", v2.Routes.Count);
        SampleConsole.Note("YARP observes the signalled token, calls GetConfig() again, and applies v2 with zero downtime.");
    }

    /// <summary>Mirrors AgentRouteProvider's static-route fallback projection.</summary>
    private static (List<RouteConfig> Routes, List<ClusterConfig> Clusters) BuildFromStaticRoutes(GatewayOptions options)
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        foreach (var entry in options.Routes)
        {
            routes.Add(new RouteConfig
            {
                RouteId = entry.RouteId,
                ClusterId = entry.RouteId,
                Match = new RouteMatch
                {
                    Path = entry.Path,
                    Hosts = entry.Host is not null ? [entry.Host] : null
                }
            });

            var destinations = new Dictionary<string, DestinationConfig>();
            for (var i = 0; i < entry.Backends.Count; i++)
                destinations[$"d{i}"] = new DestinationConfig { Address = entry.Backends[i] };

            clusters.Add(new ClusterConfig
            {
                ClusterId = entry.RouteId,
                LoadBalancingPolicy = "RoundRobin",
                Destinations = destinations
            });
        }

        return (routes, clusters);
    }

    private static string Format(IReadOnlyList<string>? hosts)
        => hosts is null || hosts.Count == 0 ? "*" : string.Join(",", hosts);

    /// <summary>
    ///     Minimal public stand-in for the internal AgentProxyConfig: an immutable snapshot whose
    ///     change token can be signalled to trigger a YARP reload.
    /// </summary>
    private sealed class SampleProxyConfig : IProxyConfig
    {
        private readonly CancellationTokenSource _cts = new();

        public SampleProxyConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        {
            Routes = routes;
            Clusters = clusters;
            ChangeToken = new CancellationChangeToken(_cts.Token);
        }

        public IReadOnlyList<RouteConfig> Routes { get; }
        public IReadOnlyList<ClusterConfig> Clusters { get; }
        public IChangeToken ChangeToken { get; }

        public void SignalChange() => _cts.Cancel();
    }
}
