namespace Pragmatic.ControlPlane;

/// <summary>
///     How a health contributor provides its status.
/// </summary>
public enum HealthContributorMode
{
    /// <summary>
    ///     The aggregator calls <see cref="IHostHealthContributor.CheckAsync"/> periodically.
    ///     The contributor does real work on each call (e.g. pings a database, checks a queue).
    /// </summary>
    Pull,

    /// <summary>
    ///     The contributor updates its own state proactively (e.g. on transport connect/disconnect).
    ///     <see cref="IHostHealthContributor.CheckAsync"/> returns the cached state instantly.
    /// </summary>
    Push,
}
