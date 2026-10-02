using Pragmatic.Logging.Filtering;

namespace Pragmatic.Logging.Tests.Filtering;

/// <summary>
/// Direct unit tests for the three internal <see cref="IRateLimiterStrategy"/> implementations
/// (token bucket, sliding window, fixed window) that back the high-performance logging rate limiter.
/// </summary>
public class RateLimiterStrategyTests
{
    // =========================================================================
    // Token bucket
    // =========================================================================

    [Fact]
    public void TokenBucket_AllowsBurstUpToCapacity_ThenDenies()
    {
        // Window large enough that continuous refill is negligible across an immediate burst.
        var limiter = new TokenBucketRateLimiter(maxMessages: 5, timeWindow: TimeSpan.FromMinutes(1));

        for (var i = 0; i < 5; i++)
            Assert.True(limiter.ShouldAllow(), $"token {i} within capacity should be allowed");

        Assert.False(limiter.ShouldAllow());
    }

    [Fact]
    public async Task TokenBucket_RefillsOverTime()
    {
        var limiter = new TokenBucketRateLimiter(maxMessages: 2, timeWindow: TimeSpan.FromMilliseconds(200));

        Assert.True(limiter.ShouldAllow());
        Assert.True(limiter.ShouldAllow());
        Assert.False(limiter.ShouldAllow());

        // A full window restores the full capacity.
        await Task.Delay(260);

        Assert.True(limiter.ShouldAllow());
    }

    [Fact]
    public void TokenBucket_ZeroCapacity_AlwaysDenies()
    {
        var limiter = new TokenBucketRateLimiter(maxMessages: 0, timeWindow: TimeSpan.FromMinutes(1));

        Assert.False(limiter.ShouldAllow());
    }

    [Fact]
    public async Task TokenBucket_IsThreadSafe_AllowsExactlyCapacityOnBurst()
    {
        var limiter = new TokenBucketRateLimiter(maxMessages: 100, timeWindow: TimeSpan.FromMinutes(1));
        var allowed = 0;

        var tasks = Enumerable.Range(0, 150).Select(_ => Task.Run(() =>
        {
            if (limiter.ShouldAllow())
                Interlocked.Increment(ref allowed);
        }));

        await Task.WhenAll(tasks);

        Assert.Equal(100, allowed);
    }

    // =========================================================================
    // Sliding window
    // =========================================================================

    [Fact]
    public void SlidingWindow_AllowsUpToMax_ThenDenies()
    {
        var limiter = new SlidingWindowRateLimiter(maxMessages: 3, timeWindow: TimeSpan.FromMinutes(1));

        Assert.True(limiter.ShouldAllow());
        Assert.True(limiter.ShouldAllow());
        Assert.True(limiter.ShouldAllow());
        Assert.False(limiter.ShouldAllow());
    }

    [Fact]
    public async Task SlidingWindow_EvictsAgedEntries()
    {
        var limiter = new SlidingWindowRateLimiter(maxMessages: 2, timeWindow: TimeSpan.FromMilliseconds(150));

        Assert.True(limiter.ShouldAllow());
        Assert.True(limiter.ShouldAllow());
        Assert.False(limiter.ShouldAllow());

        // Older timestamps slide out of the trailing window.
        await Task.Delay(200);

        Assert.True(limiter.ShouldAllow());
    }

    [Fact]
    public void SlidingWindow_ZeroMax_AlwaysDenies()
    {
        var limiter = new SlidingWindowRateLimiter(maxMessages: 0, timeWindow: TimeSpan.FromMinutes(1));

        Assert.False(limiter.ShouldAllow());
    }

    // =========================================================================
    // Fixed window
    // =========================================================================

    [Fact]
    public void FixedWindow_AllowsUpToMax_ThenDenies()
    {
        var limiter = new FixedWindowRateLimiter(maxMessages: 2, timeWindow: TimeSpan.FromMinutes(1));

        Assert.True(limiter.ShouldAllow());
        Assert.True(limiter.ShouldAllow());
        Assert.False(limiter.ShouldAllow());
    }

    [Fact]
    public async Task FixedWindow_ResetsAtBoundary()
    {
        var limiter = new FixedWindowRateLimiter(maxMessages: 2, timeWindow: TimeSpan.FromMilliseconds(150));

        Assert.True(limiter.ShouldAllow());
        Assert.True(limiter.ShouldAllow());
        Assert.False(limiter.ShouldAllow());

        await Task.Delay(200);

        // New window — counter reset.
        Assert.True(limiter.ShouldAllow());
        Assert.True(limiter.ShouldAllow());
        Assert.False(limiter.ShouldAllow());
    }

    [Fact]
    public void FixedWindow_ZeroMax_AlwaysDenies()
    {
        var limiter = new FixedWindowRateLimiter(maxMessages: 0, timeWindow: TimeSpan.FromMinutes(1));

        Assert.False(limiter.ShouldAllow());
    }
}
