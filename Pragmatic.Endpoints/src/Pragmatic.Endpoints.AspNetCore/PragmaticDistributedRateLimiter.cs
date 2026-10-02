using System.Threading.RateLimiting;
using Pragmatic.Caching;

namespace Pragmatic.Endpoints.AspNetCore;

/// <summary>
///     A rate limiter backed by Pragmatic.Caching's <c>ICacheStack</c>, using the atomic
///     increment-and-check pattern (<see cref="ICacheStack.IncrementAsync"/>).
/// </summary>
/// <remarks>
///     <para>
///         Fixed-window implementation using cache TTL for window expiry. The counter key is
///         <c>ratelimit:{partitionKey}:{windowId}</c>; the window ID is UTC ticks divided by
///         the window size.
///     </para>
///     <para>
///         Enforcement is exact within a single instance (the in-process <c>ICacheStack</c> is
///         atomic per key). For strict <b>cross-instance</b> enforcement, add the
///         <c>Pragmatic.Caching.Redis</c> package and call
///         <c>services.AddRedisAtomicCounters(connectionString)</c>: it decorates the cache stack
///         so <see cref="ICacheStack.IncrementAsync"/> runs as a single atomic Redis script.
///     </para>
/// </remarks>
public sealed class PragmaticDistributedRateLimiter(
    ICacheStack cache,
    string partitionKey,
    int permitLimit,
    TimeSpan window)
    : RateLimiter
{
    public override TimeSpan? IdleDuration => null;

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        // Sync path — distributed rate limiting requires async I/O; deny synchronous attempts
        // rather than silently granting them (which would bypass all limits on the sync code path).
        return new DeniedLease(window);
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
        int permitCount, CancellationToken cancellationToken)
    {
        // Propagate cancellation before touching the cache.
        cancellationToken.ThrowIfCancellationRequested();

        var windowId = DateTimeOffset.UtcNow.Ticks / window.Ticks;
        var key = $"ratelimit:{partitionKey}:{windowId}";

        try
        {
            // Atomic INCR pattern: add this request's permits and inspect the resulting count.
            // The in-process ICacheStack is atomic per key, so concurrent requests in the same
            // window cannot both read a below-limit value and both be admitted.
            var newCount = await cache.IncrementAsync(key, permitCount, window, cancellationToken)
                .ConfigureAwait(false);

            if (newCount > permitLimit)
            {
                // We over-counted by reserving permits we won't use. Give them back so a parallel
                // request that is still under the limit isn't pushed over by ours. The window key
                // also self-heals on expiry, so a failed decrement only briefly inflates the count.
                await cache.IncrementAsync(key, -permitCount, window, cancellationToken)
                    .ConfigureAwait(false);
                return new DeniedLease(window);
            }

            return new GrantedLease();
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation — do not swallow.
            throw;
        }
        catch (Exception)
        {
            // On non-cancellation cache failure, fail closed (deny) to avoid bypassing limits.
            return new DeniedLease(window);
        }
    }

    private sealed class GrantedLease : RateLimitLease
    {
        public override bool IsAcquired => true;

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }

        public override IEnumerable<string> MetadataNames => [];
    }

    private sealed class DeniedLease(TimeSpan retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = retryAfter;
                return true;
            }

            metadata = null;
            return false;
        }

        public override IEnumerable<string> MetadataNames => [MetadataName.RetryAfter.Name];
    }
}
