using Microsoft.Extensions.DependencyInjection;
using Pragmatic.ControlPlane;

namespace Pragmatic.Messaging.Entities;

/// <summary>
///     Pull-based health contributor for the outbox.
///     Checks pending message count across all outbox sources.
/// </summary>
public sealed class OutboxHealthContributor(IServiceScopeFactory scopeFactory) : IHostHealthContributor
{
    public string Name => "Messaging.Outbox";
    public string? Category => "Messaging";
    public HealthContributorMode Mode => HealthContributorMode.Pull;

    /// <summary>Max pending messages before degraded. Default: 1000.</summary>
    public int PendingThreshold { get; set; } = 1000;

    public async Task<ContributorHealthReport> CheckAsync(CancellationToken ct = default)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var sources = scope.ServiceProvider.GetServices<IOutboxSource>();

        var totalPending = 0;
        var data = new Dictionary<string, object>();

        foreach (var source in sources)
        {
            // Read-only inspection — never CLAIM rows from a health probe (that would starve the pump).
            var (count, _) = await source.InspectPendingAsync(ct).ConfigureAwait(false);
            totalPending += count;
            data[$"pending.{source.BoundaryName}"] = count;
        }

        data["totalPending"] = totalPending;

        if (totalPending > PendingThreshold)
            return ContributorHealthReport.Degraded(
                $"Outbox has {totalPending} pending messages (threshold: {PendingThreshold})", data);

        return new ContributorHealthReport
        {
            Status = ContributorHealthStatus.Healthy,
            Message = $"Outbox operational, {totalPending} pending",
            Data = data,
        };
    }
}
