namespace Pragmatic.Gateway;

/// <summary>
///     The versioned key schema the Gateway and the Agent share on the Agent KV. This is the single
///     contract for gateway coordination keys — external writers (the Agent CLI / control tooling) MUST
///     use these keys, and the Gateway reads them.
/// </summary>
/// <remarks>
///     Ownership: <see cref="RoutesPrefix"/> / <see cref="ClustersPrefix"/> are gateway-owned (written by
///     control tooling, read by the Gateway). <see cref="HostRosterPrefix"/> is owned by the Agent
///     host-lifecycle (written by the daemon on Register/Heartbeat, read here for per-app maintenance).
///     <see cref="FullMaintenanceKey"/> is an operator toggle. Bump <see cref="SchemaVersion"/> on a
///     breaking change to any value shape.
/// </remarks>
public static class GatewayKvSchema
{
    /// <summary>Schema version (semantic). A major bump signals an incompatible key/value change.</summary>
    /// <remarks>2.0.0: the host roster is keyed per instance, <c>state/app:{appId}/{instanceId}</c>.</remarks>
    public const string SchemaVersion = "2.0.0";

    /// <summary>Prefix common to all gateway-owned coordination keys.</summary>
    public const string Prefix = "gateway/";

    /// <summary><c>gateway/routes/{routeId}</c> = route definition JSON (path, clusterId, requireAuth, hosts).</summary>
    public const string RoutesPrefix = "gateway/routes/";

    /// <summary><c>gateway/clusters/{clusterId}</c> = cluster definition JSON (destinations).</summary>
    public const string ClustersPrefix = "gateway/clusters/";

    /// <summary>
    ///     <c>gateway/instances/{routeId}/{instanceId}</c> = an instance announcement (address and route), written
    ///     ephemeral by each running host. Defined with the Agent protocol, because hosts write it.
    /// </summary>
    public const string InstancesPrefix = Pragmatic.Agent.Protocol.Payloads.InstanceAnnouncementKeys.Prefix;

    /// <summary>
    ///     <c>state/app:{appId}/{instanceId}</c> = host roster descriptor JSON, one per running instance
    ///     (Agent-owned; read here for maintenance).
    /// </summary>
    public const string HostRosterPrefix = Pragmatic.Agent.Protocol.Payloads.HostRosterKeys.Prefix;

    /// <summary><c>state/gateway/maintenance</c> = <c>"true"</c>/<c>"false"</c>, the operator full-gateway toggle.</summary>
    public const string FullMaintenanceKey = "state/gateway/maintenance";

    /// <summary>Key for a route definition.</summary>
    public static string RouteKey(string routeId) => RoutesPrefix + routeId;

    /// <summary>Key for a cluster definition.</summary>
    public static string ClusterKey(string clusterId) => ClustersPrefix + clusterId;
}
