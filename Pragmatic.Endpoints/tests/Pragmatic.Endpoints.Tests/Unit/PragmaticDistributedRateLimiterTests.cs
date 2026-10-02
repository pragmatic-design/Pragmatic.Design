using System.Threading.RateLimiting;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Caching;
using Pragmatic.Endpoints.AspNetCore;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

public class PragmaticDistributedRateLimiterTests
{
    private static PragmaticDistributedRateLimiter Create(
        ICacheStack cache, int permitLimit = 5, TimeSpan? window = null)
        => new(cache, "partition-a", permitLimit, window ?? TimeSpan.FromMinutes(1));

    [Fact]
    public void AttemptAcquire_SyncPath_DeniesLease()
    {
        var cache = new CacheStackMock();
        var limiter = Create(cache);

        using var lease = limiter.AttemptAcquire(1);

        lease.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public void AttemptAcquire_SyncPath_DeniedLeaseExposesRetryAfter()
    {
        var cache = new CacheStackMock();
        var window = TimeSpan.FromSeconds(30);
        var limiter = Create(cache, window: window);

        using var lease = limiter.AttemptAcquire(1);

        lease.TryGetMetadata(MetadataName.RetryAfter.Name, out var retryAfter).Should().BeTrue();
        retryAfter.Should().Be(window);
    }

    [Fact]
    public async Task AcquireAsync_FirstRequestInWindow_GrantsLease()
    {
        var cache = new CacheStackMock();
        cache.IncrementAsync.Returns(new ValueTask<long>(1L));
        var limiter = Create(cache, permitLimit: 5);

        using var lease = await limiter.AcquireAsync(1);

        lease.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_FirstRequest_IncrementsCounterByPermitCount()
    {
        var cache = new CacheStackMock();
        cache.IncrementAsync.Returns(new ValueTask<long>(1L));
        var limiter = Create(cache, permitLimit: 5);

        using var lease = await limiter.AcquireAsync(1);

        cache.IncrementAsync.Received(1, Arg.Is<string>(k => k.StartsWith("ratelimit:partition-a:")), 1L, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcquireAsync_CountBelowLimit_GrantsLease()
    {
        var cache = new CacheStackMock();
        cache.IncrementAsync.Returns(new ValueTask<long>(3L));
        var limiter = Create(cache, permitLimit: 5);

        using var lease = await limiter.AcquireAsync(1);

        lease.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_CountAtLimit_GrantsLease()
    {
        var cache = new CacheStackMock();
        cache.IncrementAsync.Returns(new ValueTask<long>(5L));
        var limiter = Create(cache, permitLimit: 5);

        using var lease = await limiter.AcquireAsync(1);

        lease.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_CountExceedsLimit_DeniesLeaseAndRollsBack()
    {
        var cache = new CacheStackMock();
        cache.IncrementAsync.Returns(new ValueTask<long>(6L));
        var limiter = Create(cache, permitLimit: 5);

        using var lease = await limiter.AcquireAsync(1);

        lease.IsAcquired.Should().BeFalse();
        // Reserved permits are returned when the request is rejected.
        cache.IncrementAsync.Received(1, Arg.Any<string>(), -1L, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcquireAsync_MultiPermitExceedsLimit_DeniesLease()
    {
        var cache = new CacheStackMock();
        // 3 existing + 3 requested = 6 > 5 → denied
        cache.IncrementAsync.When(Arg.Any<string>(), 3L, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
.Returns(new ValueTask<long>(6L));
        var limiter = Create(cache, permitLimit: 5);

        using var lease = await limiter.AcquireAsync(3);

        lease.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task AcquireAsync_CacheThrows_FailsClosedWithDeniedLease()
    {
        var cache = new CacheStackMock();
        cache.IncrementAsync.Throws(new InvalidOperationException("cache outage"));
        var limiter = Create(cache);

        using var lease = await limiter.AcquireAsync(1);

        lease.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task AcquireAsync_AlreadyCancelled_ThrowsOperationCanceled()
    {
        var cache = new CacheStackMock();
        var limiter = Create(cache);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await limiter.AcquireAsync(1, cts.Token).ConfigureAwait(false);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AcquireAsync_CacheThrowsCancellation_PropagatesNotSwallowed()
    {
        var cache = new CacheStackMock();
        cache.IncrementAsync.Throws(new OperationCanceledException());
        var limiter = Create(cache);

        var act = async () => await limiter.AcquireAsync(1).ConfigureAwait(false);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // A non-atomic read-modify-write would let concurrent requests in the same window both read a
    // below-limit count and both be admitted. Driving N parallel
    // AcquireAsync calls through the REAL in-process atomic ICacheStack must admit at most
    // permitLimit leases.
    [Theory]
    [InlineData(1, 64)]
    [InlineData(3, 64)]
    public async Task AcquireAsync_ConcurrentRequests_AdmitsAtMostPermitLimit(int permitLimit, int concurrency)
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        ICacheStack cache = new HybridCacheStack(provider.GetRequiredService<HybridCache>());

        // Long window so all requests fall in the same window bucket.
        var limiter = new PragmaticDistributedRateLimiter(
            cache, "concurrent-partition", permitLimit, TimeSpan.FromMinutes(10));

        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => AcquireWhenSignalled(limiter, start.Task))
            .ToArray();

        start.SetResult();
        var results = await Task.WhenAll(tasks);

        results.Count(acquired => acquired).Should().Be(permitLimit);
    }

    // Worker for the concurrency test; extracted from the test method so its internal awaits can
    // use ConfigureAwait(false) (CA2007) without tripping the xUnit1030 "no ConfigureAwait in a
    // test method" analyzer.
    private static async Task<bool> AcquireWhenSignalled(
        PragmaticDistributedRateLimiter limiter, Task gate)
    {
        await gate.ConfigureAwait(false);
        using var lease = await limiter.AcquireAsync(1).ConfigureAwait(false);
        return lease.IsAcquired;
    }

    [Fact]
    public void IdleDuration_IsNull()
    {
        var cache = new CacheStackMock();
        var limiter = Create(cache);

        limiter.IdleDuration.Should().BeNull();
    }

    [Fact]
    public void GetStatistics_ReturnsNull()
    {
        var cache = new CacheStackMock();
        var limiter = Create(cache);

        limiter.GetStatistics().Should().BeNull();
    }
}
