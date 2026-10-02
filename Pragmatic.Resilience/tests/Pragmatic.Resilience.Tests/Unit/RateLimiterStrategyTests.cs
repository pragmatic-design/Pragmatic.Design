using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Time.Testing;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Tests.Unit;

public class RateLimiterStrategyTests
{
    private static ResilienceContext CreateContext(string name = "TestOp")
        => new() { OperationName = name };

    [Fact]
    public async Task WithinLimit_ExecutesNormally()
    {
        var strategy = new RateLimiterStrategy(new RateLimiterOptions { MaxRequests = 5, Window = TimeSpan.FromMinutes(1) });

        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(42),
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task ExceedsLimit_ThrowsRateLimitRejectedException()
    {
        var strategy = new RateLimiterStrategy(new RateLimiterOptions { MaxRequests = 2, Window = TimeSpan.FromMinutes(1) });

        // First two should succeed
        await strategy.ExecuteAsync<int>((ctx, ct) => Task.FromResult(1), CreateContext(), CancellationToken.None);
        await strategy.ExecuteAsync<int>((ctx, ct) => Task.FromResult(2), CreateContext(), CancellationToken.None);

        // Third should be rejected
        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(3),
            CreateContext(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<RateLimitRejectedException>();
        ex.Which.MaxRequests.Should().Be(2);
        ex.Which.Window.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task WindowExpiry_AllowsNewRequests()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var strategy = new RateLimiterStrategy(
            new RateLimiterOptions { MaxRequests = 1, Window = TimeSpan.FromSeconds(10) },
            fakeTime);

        // First succeeds
        await strategy.ExecuteAsync<int>((ctx, ct) => Task.FromResult(1), CreateContext(), CancellationToken.None);

        // Second is rejected
        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(2),
            CreateContext(), CancellationToken.None);
        await act.Should().ThrowAsync<RateLimitRejectedException>();

        // Advance past window
        fakeTime.Advance(TimeSpan.FromSeconds(11));

        // Now should succeed again
        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(3),
            CreateContext(), CancellationToken.None);

        result.Should().Be(3);
    }

    [Fact]
    public async Task SlidingWindow_OnlyCountsRecentRequests()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var strategy = new RateLimiterStrategy(
            new RateLimiterOptions { MaxRequests = 2, Window = TimeSpan.FromSeconds(10) },
            fakeTime);

        // Request at t=0
        await strategy.ExecuteAsync<int>((ctx, ct) => Task.FromResult(1), CreateContext(), CancellationToken.None);

        // Request at t=6 (within window)
        fakeTime.Advance(TimeSpan.FromSeconds(6));
        await strategy.ExecuteAsync<int>((ctx, ct) => Task.FromResult(2), CreateContext(), CancellationToken.None);

        // At t=6 we have 2 requests, third is rejected
        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(3),
            CreateContext(), CancellationToken.None);
        await act.Should().ThrowAsync<RateLimitRejectedException>();

        // Advance to t=11 — first request (t=0) falls out of window
        fakeTime.Advance(TimeSpan.FromSeconds(5));

        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(4),
            CreateContext(), CancellationToken.None);
        result.Should().Be(4);
    }

    [Fact]
    public void MaxRequests_LessThanOne_Throws()
    {
        var act = () => new RateLimiterStrategy(new RateLimiterOptions { MaxRequests = 0 });
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Order_Is50()
    {
        var strategy = new RateLimiterStrategy(new RateLimiterOptions());
        strategy.Order.Should().Be(StrategyOrder.RateLimiter);
    }

    [Fact]
    public async Task OperationStillExecutes_WhenWithinLimit()
    {
        var executed = false;
        var strategy = new RateLimiterStrategy(new RateLimiterOptions { MaxRequests = 10, Window = TimeSpan.FromMinutes(1) });

        await strategy.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                executed = true;
                return Task.FromResult(1);
            },
            CreateContext(), CancellationToken.None);

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task CancellationToken_IsRespected()
    {
        var strategy = new RateLimiterStrategy(new RateLimiterOptions());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(1),
            CreateContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
