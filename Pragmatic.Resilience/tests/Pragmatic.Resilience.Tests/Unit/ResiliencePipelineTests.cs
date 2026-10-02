using Pragmatic.Testing.Assertions;
using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Tests.Unit;

public class ResiliencePipelineTests
{
    private static ResilienceContext CreateContext(string name = "TestOp")
        => new() { OperationName = name };

    [Fact]
    public async Task EmptyPipeline_ExecutesOperationDirectly()
    {
        var pipeline = new ResiliencePipeline([]);

        var result = await pipeline.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(42),
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task SingleStrategy_WrapsOperation()
    {
        var pipeline = new ResiliencePipeline([
            new RetryStrategy(new RetryOptions
            {
                MaxRetries = 2,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                UseJitter = false
            })
        ]);
        var callCount = 0;

        var result = await pipeline.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(42);
            },
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task MultipleStrategies_ComposesInOrder()
    {
        var executionOrder = new List<string>();

        var outerStrategy = new TrackingStrategy(100, "outer", executionOrder);
        var innerStrategy = new TrackingStrategy(200, "inner", executionOrder);

        var pipeline = new ResiliencePipeline([innerStrategy, outerStrategy]);

        await pipeline.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                executionOrder.Add("operation");
                return Task.FromResult(42);
            },
            CreateContext(), CancellationToken.None);

        executionOrder.Should().BeEquivalentTo(
            ["outer-before", "inner-before", "operation", "inner-after", "outer-after"],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task TimeoutAndRetry_TimeoutWrapsRetry()
    {
        var pipeline = new ResiliencePipeline([
            new TimeoutStrategy(new TimeoutOptions { Timeout = TimeSpan.FromSeconds(5) }),
            new RetryStrategy(new RetryOptions
            {
                MaxRetries = 2,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                UseJitter = false
            })
        ]);
        var callCount = 0;

        var result = await pipeline.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(42);
            },
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task VoidExecution_Works()
    {
        var pipeline = new ResiliencePipeline([]);
        var executed = false;

        await pipeline.ExecuteAsync(
            (ctx, ct) => { executed = true; return Task.CompletedTask; },
            CreateContext(), CancellationToken.None);

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_SetsTotalElapsed()
    {
        var pipeline = new ResiliencePipeline([
            new RetryStrategy(new RetryOptions { MaxRetries = 0 })
        ]);
        var context = CreateContext();

        await pipeline.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                await Task.Delay(50, ct);
                return 42;
            },
            context, CancellationToken.None);

        context.TotalElapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(1));
    }

    /// <summary>Test helper that tracks execution order.</summary>
    private sealed class TrackingStrategy(int order, string name, List<string> tracker) : IResilienceStrategy
    {
        public int Order => order;

        public async Task<TResult> ExecuteAsync<TResult>(
            Func<ResilienceContext, CancellationToken, Task<TResult>> next,
            ResilienceContext context,
            CancellationToken ct)
        {
            tracker.Add($"{name}-before");
            var result = await next(context, ct);
            tracker.Add($"{name}-after");
            return result;
        }
    }
    [Fact]
    public async Task FallbackWithRetry_RetryRunsBeforeFallback()
    {
        // Regression (RVW-8): fallback must be OUTERMOST. With retry configured, a transient
        // failure must be retried to success — the fallback value must NOT short-circuit retry.
        var attempts = 0;
        var pipeline = new ResiliencePipeline(new IResilienceStrategy[]
        {
            new RetryStrategy(new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) }),
            new FallbackStrategy<int>(new FallbackOptions<int>
            {
                FallbackAction = (ex, ctx, ct) => Task.FromResult(-1)
            })
        });

        var result = await pipeline.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                attempts++;
                if (attempts < 3) throw new InvalidOperationException("transient");
                return Task.FromResult(42);
            },
            new ResilienceContext { OperationName = "op" });

        result.Should().Be(42, "retry must observe the failures and eventually succeed");
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task FallbackWithRetry_RetryExhausted_FallsBack()
    {
        var pipeline = new ResiliencePipeline(new IResilienceStrategy[]
        {
            new RetryStrategy(new RetryOptions { MaxRetries = 1, BaseDelay = TimeSpan.FromMilliseconds(1) }),
            new FallbackStrategy<int>(new FallbackOptions<int>
            {
                FallbackAction = (ex, ctx, ct) => Task.FromResult(-1)
            })
        });

        var result = await pipeline.ExecuteAsync<int>(
            (ctx, ct) => throw new InvalidOperationException("always fails"),
            new ResilienceContext { OperationName = "op" });

        result.Should().Be(-1, "fallback is the last resort once retry is exhausted");
    }
}
