using Microsoft.Extensions.Logging;
using Pragmatic.Resilience.State;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Pipeline;

/// <summary>
/// Fluent builder for composing resilience strategies into a pipeline.
/// </summary>
public sealed class ResiliencePipelineBuilder
{
    private readonly List<IResilienceStrategy> _strategies = [];

    /// <summary>Add retry strategy with options callback.</summary>
    public ResiliencePipelineBuilder AddRetry(Action<RetryOptions>? configure = null, ILogger? logger = null)
    {
        var options = new RetryOptions();
        configure?.Invoke(options);
        _strategies.Add(new RetryStrategy(options, logger));
        return this;
    }

    /// <summary>Add retry strategy from existing options (no copy).</summary>
    internal ResiliencePipelineBuilder AddRetry(RetryOptions options, ILogger? logger = null)
    {
        _strategies.Add(new RetryStrategy(options, logger));
        return this;
    }

    /// <summary>Add timeout strategy with options callback.</summary>
    public ResiliencePipelineBuilder AddTimeout(Action<TimeoutOptions>? configure = null, ILogger? logger = null)
    {
        var options = new TimeoutOptions();
        configure?.Invoke(options);
        _strategies.Add(new TimeoutStrategy(options, logger));
        return this;
    }

    /// <summary>Add timeout strategy from existing options (no copy).</summary>
    internal ResiliencePipelineBuilder AddTimeout(TimeoutOptions options, ILogger? logger = null)
    {
        _strategies.Add(new TimeoutStrategy(options, logger));
        return this;
    }

    /// <summary>Add circuit breaker strategy with options callback.</summary>
    public ResiliencePipelineBuilder AddCircuitBreaker(
        ICircuitBreakerStateStore stateStore,
        Action<CircuitBreakerOptions>? configure = null,
        ILogger? logger = null)
    {
        var options = new CircuitBreakerOptions();
        configure?.Invoke(options);
        _strategies.Add(new CircuitBreakerStrategy(options, stateStore, logger));
        return this;
    }

    /// <summary>Add circuit breaker strategy from existing options (no copy).</summary>
    internal ResiliencePipelineBuilder AddCircuitBreaker(
        ICircuitBreakerStateStore stateStore,
        CircuitBreakerOptions options,
        ILogger? logger = null)
    {
        _strategies.Add(new CircuitBreakerStrategy(options, stateStore, logger));
        return this;
    }

    /// <summary>Add bulkhead (concurrency limiter) strategy with options callback.</summary>
    public ResiliencePipelineBuilder AddBulkhead(Action<BulkheadOptions>? configure = null, ILogger? logger = null)
    {
        var options = new BulkheadOptions();
        configure?.Invoke(options);
        _strategies.Add(new BulkheadStrategy(options, logger));
        return this;
    }

    /// <summary>Add bulkhead strategy from existing options (no copy).</summary>
    internal ResiliencePipelineBuilder AddBulkhead(BulkheadOptions options, ILogger? logger = null)
    {
        _strategies.Add(new BulkheadStrategy(options, logger));
        return this;
    }

    /// <summary>Add hedging (parallel execution, first-wins) strategy.</summary>
    public ResiliencePipelineBuilder AddHedging(Action<HedgingOptions>? configure = null, ILogger? logger = null)
    {
        var options = new HedgingOptions();
        configure?.Invoke(options);
        _strategies.Add(new HedgingStrategy(options, logger));
        return this;
    }

    /// <summary>Add hedging strategy from existing options (no copy).</summary>
    internal ResiliencePipelineBuilder AddHedging(HedgingOptions options, ILogger? logger = null)
    {
        _strategies.Add(new HedgingStrategy(options, logger));
        return this;
    }

    /// <summary>Add rate limiter (requests per time window) strategy.</summary>
    public ResiliencePipelineBuilder AddRateLimiter(Action<RateLimiterOptions>? configure = null, ILogger? logger = null)
    {
        var options = new RateLimiterOptions();
        configure?.Invoke(options);
        _strategies.Add(new RateLimiterStrategy(options, logger: logger));
        return this;
    }

    /// <summary>Add rate limiter strategy from existing options (no copy).</summary>
    internal ResiliencePipelineBuilder AddRateLimiter(RateLimiterOptions options, ILogger? logger = null)
    {
        _strategies.Add(new RateLimiterStrategy(options, logger: logger));
        return this;
    }

    /// <summary>Add fallback strategy for a specific result type.</summary>
    public ResiliencePipelineBuilder AddFallback<TResult>(Func<CancellationToken, Task<TResult>> fallbackAction)
    {
        _strategies.Add(new FallbackStrategy<TResult>(new FallbackOptions<TResult>
        {
            FallbackAction = (_, _, ct) => fallbackAction(ct)
        }));
        return this;
    }

    /// <summary>Add fallback strategy with full options.</summary>
    public ResiliencePipelineBuilder AddFallback<TResult>(FallbackOptions<TResult> options)
    {
        _strategies.Add(new FallbackStrategy<TResult>(options));
        return this;
    }

    /// <summary>
    ///     Add a fallback for <b>void</b> operations (those executed via the non-generic
    ///     <see cref="IResiliencePipeline.ExecuteAsync(Func{ResilienceContext, CancellationToken, Task}, ResilienceContext, CancellationToken)"/>
    ///     overload). A typed <c>AddFallback&lt;TResult&gt;</c> never matches a void execution, so use this
    ///     overload to run compensating work (log, enqueue, notify) when a void operation fails.
    /// </summary>
    public ResiliencePipelineBuilder AddFallback(Func<Exception, CancellationToken, Task> fallbackAction)
    {
        // Void executions are represented internally as Task<int> (unit = 0), so the fallback must be
        // registered against that unit type to intercept them.
        _strategies.Add(new FallbackStrategy<int>(new FallbackOptions<int>
        {
            FallbackAction = async (ex, _, ct) =>
            {
                await fallbackAction(ex, ct).ConfigureAwait(false);
                return 0;
            }
        }));
        return this;
    }

    /// <summary>Add a custom strategy.</summary>
    public ResiliencePipelineBuilder AddStrategy(IResilienceStrategy strategy)
    {
        _strategies.Add(strategy);
        return this;
    }

    /// <summary>Build the configured pipeline.</summary>
    public IResiliencePipeline Build()
    {
        if (_strategies.Count == 0)
            return PassthroughPipeline.Instance;

        return new ResiliencePipeline(_strategies);
    }
}
