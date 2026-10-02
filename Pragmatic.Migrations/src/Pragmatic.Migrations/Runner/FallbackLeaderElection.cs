using Microsoft.Extensions.Logging;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Wraps a <see cref="IMigrationLeaderElection"/> with a <b>fail-safe</b> fallback.
///     If the inner election throws (e.g. database not reachable), this instance must NEVER
///     assume leadership: doing so on a transient DB error would let every pod run migrations
///     simultaneously (split-brain). On failure we log and return <c>false</c> (not-leader),
///     so the instance falls back to waiting for whoever genuinely holds the lock.
/// </summary>
internal sealed class FallbackLeaderElection(
    IMigrationLeaderElection inner,
    ILogger logger) : IMigrationLeaderElection
{
    public async Task<bool> TryBecomeLeaderAsync(CancellationToken ct = default)
    {
        // Cancellation must propagate cleanly — never get masked into a leadership decision.
        try
        {
            return await inner.TryBecomeLeaderAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // FAIL SAFE: a failure to acquire leadership defaults to NOT leader.
            // Returning true here would cause split-brain (every pod migrating at once).
            logger.LogWarning(ex, "Leader election failed — defaulting to NOT leader (fail-safe)");
            return false;
        }
    }

    public async Task ReleaseLeadershipAsync(CancellationToken ct = default)
    {
        try
        {
            await inner.ReleaseLeadershipAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Leader release failed — lock will expire");
        }
    }

    public async Task WaitForLeaderCompletionAsync(CancellationToken ct = default)
    {
        try
        {
            await inner.WaitForLeaderCompletionAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A follower that cannot poll simply stops waiting and returns NoChanges; it never
            // runs migrations itself, so the leader's advisory lock remains the source of truth.
            logger.LogWarning(ex, "Leader wait failed — follower will stop waiting without migrating (fallback)");
        }
    }
}
