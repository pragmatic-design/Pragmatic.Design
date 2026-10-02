namespace Pragmatic.Gateway.Resilience;

/// <summary>
///     Resilience configuration for Gateway proxy forwarding.
///     Circuit breaker is per-cluster (each backend has its own circuit).
/// </summary>
public sealed class GatewayResilienceOptions
{
    /// <summary>Whether resilience is enabled. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Default policy applied to all clusters unless overridden.</summary>
    public ClusterResiliencePolicy Default { get; set; } = new();

    /// <summary>Per-cluster policy overrides. Key = cluster ID.</summary>
    public Dictionary<string, ClusterResiliencePolicy> Clusters { get; set; } = [];
}
