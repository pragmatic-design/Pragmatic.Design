using Pragmatic.ControlPlane;

namespace Pragmatic.Composition.ControlPlane;

/// <summary>
///     Default aggregator that collects health reports from all registered
///     <see cref="IHostHealthContributor"/> instances.
///     All contributors are checked in parallel via Task.WhenAll.
/// </summary>
public sealed class HostHealthAggregator(IEnumerable<IHostHealthContributor> contributors) : IHostHealthAggregator
{
    /// <inheritdoc />
    public async Task<AggregatedHealthReport> GetReportAsync(CancellationToken ct = default)
    {
        var contributorList = contributors.ToList();

        // Fan-out: check all contributors in parallel
        var tasks = contributorList.Select(async c =>
        {
            try
            {
                var report = await c.CheckAsync(ct).ConfigureAwait(false);
                return (c.Name, report);
            }
            catch (Exception ex)
            {
                return (c.Name, ContributorHealthReport.Unhealthy($"Check failed: {ex.Message}"));
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var reports = new Dictionary<string, ContributorHealthReport>(results.Length);
        var worstStatus = ContributorHealthStatus.Healthy;

        foreach (var (name, report) in results)
        {
            reports[name] = report;
            if (report.Status > worstStatus)
                worstStatus = report.Status;
        }

        return new AggregatedHealthReport
        {
            OverallStatus = worstStatus,
            Contributors = reports,
        };
    }
}
