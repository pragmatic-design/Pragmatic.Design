namespace Pragmatic.ControlPlane;

/// <summary>
///     Health status reported by a <see cref="IHostHealthContributor"/>.
/// </summary>
public enum ContributorHealthStatus
{
    /// <summary>The component is fully operational.</summary>
    Healthy,

    /// <summary>The component is operational but experiencing issues (e.g. high latency, backlog).</summary>
    Degraded,

    /// <summary>The component is not operational.</summary>
    Unhealthy,
}
