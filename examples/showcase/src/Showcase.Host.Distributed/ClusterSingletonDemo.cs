using Pragmatic.ControlPlane;

namespace Showcase.Host.Distributed;

/// <summary>
///     Reference consumer of <see cref="IClusterLeadership" /> — the KV-lease cluster election. Across a
///     multi-host cluster only the single elected leader runs the guarded work; the other hosts stand by
///     until the lease expires and one of them takes over. In monolith / L0 mode the default
///     <c>NoOpClusterLeadership</c> makes the sole host the leader, so the work always runs.
/// </summary>
/// <remarks>
///     This exists so the election primitive has a real, demonstrated consumer end to end rather than
///     being a defined-but-unused capability: real cluster-wide singleton work (a periodic cleanup sweep,
///     a scheduled digest, a single-writer reconciler) goes exactly where the log line is. It is
///     deliberately NOT used for deploys (an external CLI orchestrator owns those) nor for migrations
///     (those keep the DB advisory lock, which is split-brain-safe in a way a gossip lease is not).
/// </remarks>
public sealed class ClusterSingletonDemo(
    IClusterLeadership leadership,
    ILogger<ClusterSingletonDemo> logger) : BackgroundService
{
    private const string Scope = "showcase-cluster-singleton";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // TryAcquire also renews an existing lease (renew-if-mine), so a host that already leads stays
            // leader; a host that doesn't stands by. IsLeader covers the window between renewals.
            var isLeader = await leadership.TryAcquireAsync(Scope, stoppingToken).ConfigureAwait(false)
                           || leadership.IsLeader(Scope);

            if (isLeader)
                logger.LogInformation(
                    "[cluster-singleton] This host holds leadership for '{Scope}' — running singleton work.",
                    Scope);
            else
                logger.LogDebug("[cluster-singleton] Another host leads '{Scope}'; standing by.", Scope);

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break; // Shutting down.
            }
        }

        // Release promptly on shutdown so a surviving host can take over without waiting for lease expiry.
        await leadership.ReleaseAsync(Scope, CancellationToken.None).ConfigureAwait(false);
    }
}
