namespace Pragmatic.ControlPlane;

/// <summary>
///     Contributes health status to the host's composite health report.
///     Each module, transport, or external dependency can register one or more contributors.
/// </summary>
/// <remarks>
///     <para>
///     Contributors are auto-discovered by the SG and registered in DI.
///     The <see cref="IHostHealthAggregator"/> collects all reports and updates
///     <see cref="IHostStatus"/> which flows to the control plane heartbeat.
///     </para>
///     <para>
///     <b>Pull mode</b>: <see cref="CheckAsync"/> is called periodically by the aggregator.
///     Do real work here (ping DB, check queue depth, etc.).
///     </para>
///     <para>
///     <b>Push mode</b>: Update internal state proactively (e.g. on connect/disconnect events).
///     <see cref="CheckAsync"/> should return the cached state instantly.
///     </para>
/// </remarks>
/// <example>
/// <code>
/// // Pull-based: checks database connectivity every poll interval
/// public sealed class DatabaseHealthContributor(IDbConnectionFactory factory) : IHostHealthContributor
/// {
///     public string Name => "Database.Booking";
///     public string? Category => "Database";
///     public HealthContributorMode Mode => HealthContributorMode.Pull;
///
///     public async Task&lt;ContributorHealthReport&gt; CheckAsync(CancellationToken ct)
///     {
///         var sw = Stopwatch.StartNew();
///         await using var conn = await factory.CreateAsync(ct);
///         return ContributorHealthReport.Healthy($"Connected, {sw.ElapsedMilliseconds}ms");
///     }
/// }
///
/// // Push-based: updates state on transport connect/disconnect
/// public sealed class MyTransportHealthContributor : IHostHealthContributor
/// {
///     public string Name => "Messaging.MyTransport";
///     public string? Category => "Messaging";
///     public HealthContributorMode Mode => HealthContributorMode.Push;
///     private volatile ContributorHealthReport _last = ContributorHealthReport.Unhealthy("Not connected");
///
///     public void OnConnected() => _last = ContributorHealthReport.Healthy("Connected");
///     public void OnDisconnected() => _last = ContributorHealthReport.Unhealthy("Disconnected");
///
///     public Task&lt;ContributorHealthReport&gt; CheckAsync(CancellationToken ct) => Task.FromResult(_last);
/// }
/// </code>
/// </example>
public interface IHostHealthContributor
{
    /// <summary>
    ///     Unique name for this contributor (e.g. "Database.Booking", "Messaging.RabbitMQ").
    ///     Used as key in the aggregated report.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Optional category for grouping in dashboards (e.g. "Database", "Messaging", "Cache").
    ///     Null means uncategorized.
    /// </summary>
    string? Category { get; }

    /// <summary>
    ///     Whether this contributor is Pull (aggregator calls periodically)
    ///     or Push (contributor updates proactively, CheckAsync returns cached).
    /// </summary>
    HealthContributorMode Mode { get; }

    /// <summary>
    ///     Returns the current health status of this component.
    ///     For Pull contributors: called periodically — do real work here.
    ///     For Push contributors: return cached state instantly.
    /// </summary>
    Task<ContributorHealthReport> CheckAsync(CancellationToken ct = default);
}
