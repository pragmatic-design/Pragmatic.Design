using Pragmatic.Testing.Assertions;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Tests.Unit;

public class RetryStrategyTests
{
    private static ResilienceContext CreateContext(string name = "TestOp")
        => new() { OperationName = name };

    [Fact]
    public async Task Execute_Success_ReturnsResultWithoutRetry()
    {
        var strategy = new RetryStrategy(new RetryOptions { MaxRetries = 3 });
        var callCount = 0;

        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) => { callCount++; return Task.FromResult(42); },
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task Execute_TransientFailureThenSuccess_RetriesAndReturns()
    {
        var strategy = new RetryStrategy(new RetryOptions
        {
            MaxRetries = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            UseJitter = false
        });
        var callCount = 0;

        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                callCount++;
                if (callCount < 3)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(42);
            },
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task Execute_AllRetriesExhausted_ThrowsRetryExhaustedException()
    {
        var strategy = new RetryStrategy(new RetryOptions
        {
            MaxRetries = 2,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            UseJitter = false
        });

        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => throw new InvalidOperationException("always fails"),
            CreateContext("FailingOp"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<RetryExhaustedException>();
        ex.Which.OperationName.Should().Be("FailingOp");
        ex.Which.Attempts.Should().Be(2);
        ex.Which.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Execute_WithShouldRetryPredicate_OnlyRetriesMatchingExceptions()
    {
        var strategy = new RetryStrategy(new RetryOptions
        {
            MaxRetries = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            UseJitter = false,
            ShouldRetry = ex => ex is InvalidOperationException
        });

        // ArgumentException should NOT be retried
        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => throw new ArgumentException("not retryable"),
            CreateContext(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Execute_Cancellation_DoesNotRetry()
    {
        var strategy = new RetryStrategy(new RetryOptions { MaxRetries = 5 });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(42),
            CreateContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Execute_ZeroRetries_DoesNotRetry()
    {
        var strategy = new RetryStrategy(new RetryOptions { MaxRetries = 0 });
        var callCount = 0;

        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                callCount++;
                throw new InvalidOperationException("fail");
            },
            CreateContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task Execute_SetsAttemptNumberOnContext()
    {
        var strategy = new RetryStrategy(new RetryOptions
        {
            MaxRetries = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            UseJitter = false
        });
        var attempts = new List<int>();
        var context = CreateContext();

        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                attempts.Add(ctx.AttemptNumber);
                if (attempts.Count < 3)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(42);
            },
            context, CancellationToken.None);

        attempts.Should().BeEquivalentTo([0, 1, 2]);
    }

    [Theory]
    [InlineData(BackoffType.Constant, 200, 200, 200)]
    [InlineData(BackoffType.Linear, 200, 400, 600)]
    [InlineData(BackoffType.Exponential, 200, 400, 800)]
    public void CalculateDelay_WithoutJitter_ReturnsCorrectDelay(
        BackoffType backoffType, double expected0, double expected1, double expected2)
    {
        var strategy = new RetryStrategy(new RetryOptions
        {
            BackoffType = backoffType,
            BaseDelay = TimeSpan.FromMilliseconds(200),
            UseJitter = false,
            MaxDelay = TimeSpan.FromSeconds(30)
        });

        strategy.CalculateDelay(0).TotalMilliseconds.Should().Be(expected0);
        strategy.CalculateDelay(1).TotalMilliseconds.Should().Be(expected1);
        strategy.CalculateDelay(2).TotalMilliseconds.Should().Be(expected2);
    }

    [Fact]
    public void CalculateDelay_ExponentialWithMaxDelay_Caps()
    {
        var strategy = new RetryStrategy(new RetryOptions
        {
            BackoffType = BackoffType.Exponential,
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(5),
            UseJitter = false
        });

        // 2^10 * 1000 = 1024000ms, but capped at 5000ms
        strategy.CalculateDelay(10).TotalMilliseconds.Should().Be(5000);
    }

    [Fact]
    public void CalculateDelay_WithJitter_VariesButWithinBounds()
    {
        var strategy = new RetryStrategy(new RetryOptions
        {
            BackoffType = BackoffType.Constant,
            BaseDelay = TimeSpan.FromMilliseconds(1000),
            UseJitter = true,
            MaxDelay = TimeSpan.FromSeconds(30)
        });

        // With jitter (0.5 - 1.5 multiplier), constant 1000ms → 500-1500ms
        var delays = Enumerable.Range(0, 100)
            .Select(_ => strategy.CalculateDelay(0).TotalMilliseconds)
            .ToList();

        delays.Should().AllSatisfy(d =>
        {
            d.Should().BeGreaterOrEqualTo(500);
            d.Should().BeLessOrEqualTo(1500);
        });

        // Should have some variance (not all the same)
        delays.Distinct().Count().Should().BeGreaterThan(1);
    }
}
