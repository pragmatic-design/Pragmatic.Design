namespace Pragmatic.ControlPlane;

/// <summary>
///     Aggregates health reports from all <see cref="IHostHealthContributor"/> instances
///     into a composite host health snapshot.
/// </summary>
public interface IHostHealthAggregator
{
    /// <summary>
    ///     Returns the latest aggregated health report from all contributors.
    ///     Pull contributors are checked on-demand; Push contributors return cached state.
    /// </summary>
    Task<AggregatedHealthReport> GetReportAsync(CancellationToken ct = default);
}

/// <summary>
///     Composite health report from all contributors.
/// </summary>
public sealed record AggregatedHealthReport
{
    /// <summary>Worst-case status across all contributors.</summary>
    public required ContributorHealthStatus OverallStatus { get; init; }

    /// <summary>Individual reports keyed by contributor name.</summary>
    public required IReadOnlyDictionary<string, ContributorHealthReport> Contributors { get; init; }

    /// <summary>When this aggregate was computed.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
