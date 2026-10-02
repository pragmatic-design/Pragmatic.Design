using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.State;
using Pragmatic.Resilience.Strategies;
// ReSharper disable once RedundantUsingDirective — IResiliencePipelineRegistry is in the parent namespace
using Pragmatic.Resilience;

namespace Pragmatic.Resilience.Configuration;

/// <summary>
/// Default implementation that resolves named resilience pipelines
/// from configuration or fluent overrides.
/// </summary>
public sealed class ResiliencePipelineProvider(
    IOptions<ResilienceOptions> options,
    ICircuitBreakerStateStore stateStore,
    ILoggerFactory? loggerFactory = null)
    : IResiliencePipelineRegistry, IDisposable
{
    private readonly ResilienceOptions _options = options.Value;

    // Lazy values so the (potentially expensive, possibly IDisposable-owning) build factory runs
    // exactly once per name even if several threads race GetOrAdd — the losing Lazy instances are
    // never forced, so no orphan pipeline is built and leaked.
    private readonly ConcurrentDictionary<string, Lazy<IResiliencePipeline>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fluent overrides — checked before configuration. Stored separately to avoid TOCTOU with cache.</summary>
    private readonly ConcurrentDictionary<string, Func<ResiliencePipelineBuilder, ResiliencePipelineBuilder>> _overrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Policy options overrides registered via AddPolicy(string, ResiliencePolicyOptions).</summary>
    private readonly ConcurrentDictionary<string, ResiliencePolicyOptions> _policyOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Operation-to-policy mapping (populated by SG or manual configuration).</summary>
    private readonly ConcurrentDictionary<string, string> _operationMappings = new(StringComparer.OrdinalIgnoreCase);

    public IResiliencePipeline GetPipeline(string policyName)
    {
        return _cache.GetOrAdd(policyName, name => new Lazy<IResiliencePipeline>(() =>
        {
            // Check fluent overrides first (TOCTOU-safe: overrides dict is authoritative)
            if (_overrides.TryGetValue(name, out var configure))
            {
                var builder = new ResiliencePipelineBuilder();
                configure(builder);
                return builder.Build();
            }

            // Check policy options overrides
            if (_policyOverrides.TryGetValue(name, out var policyOpts))
                return BuildFromOptions(policyOpts);

            // Then check configuration
            if (_options.Policies.TryGetValue(name, out var policyOptions))
                return BuildFromOptions(policyOptions);

            // Fall back to default
            if (_options.Default is not null)
                return BuildFromOptions(_options.Default);

            return PassthroughPipeline.Instance;
        })).Value;
    }

    /// <summary>
    ///     Whether <see cref="GetPipeline"/> would build something for <paramref name="policyName"/> rather
    ///     than fall through to the passthrough: a fluent or options override, a configured policy, or a
    ///     <c>Default</c> that answers every name.
    /// </summary>
    public bool IsDefined(string policyName)
        => _overrides.ContainsKey(policyName)
           || _policyOverrides.ContainsKey(policyName)
           || _options.Policies.ContainsKey(policyName)
           || _options.Default is not null;

    /// <summary>
    ///     Resolves the pipeline assigned to an operation. Lookup order:
    ///     <list type="number">
    ///         <item>If <paramref name="operationName"/> is mapped via
    ///             <c>MapOperation(operation, policy)</c>, the mapped policy wins.</item>
    ///         <item>Otherwise, the operation name is used AS the policy name —
    ///             i.e. a policy named <c>"checkout"</c> is returned for the operation
    ///             <c>"checkout"</c> when no explicit mapping exists.</item>
    ///     </list>
    ///     This fallback (step 2) is intentional: it lets callers register a policy
    ///     under the same name as the operation and skip an extra <c>MapOperation</c>
    ///     call. If neither lookup finds anything, <see cref="GetPipeline"/> then
    ///     applies its own fallback (overrides → config → default → passthrough).
    /// </summary>
    public IResiliencePipeline GetPipelineForOperation(string operationName)
    {
        if (_operationMappings.TryGetValue(operationName, out var policyName))
            return GetPipeline(policyName);

        return GetPipeline(operationName);
    }

    /// <summary>Register a fluent policy override.</summary>
    public void AddPolicy(string name, Func<ResiliencePipelineBuilder, ResiliencePipelineBuilder> configure)
    {
        // Evict before writing the override so any concurrent GetPipeline misses the stale cached entry
        // and is forced to rebuild from the new override on the next call.
        EvictAndDispose(name);
        _overrides[name] = configure;
    }

    /// <summary>Register a policy options override.</summary>
    public void AddPolicy(string name, ResiliencePolicyOptions options)
    {
        // Evict before writing so GetPipeline rebuilds from the new options.
        EvictAndDispose(name);
        _policyOverrides[name] = options;
    }

    /// <summary>Map an operation name to a policy name.</summary>
    public void MapOperation(string operationName, string policyName)
    {
        _operationMappings[operationName] = policyName;
    }

    public void Dispose()
    {
        // Iterate a snapshot so a mid-loop exception does not leave remaining pipelines undisposed.
        List<Exception>? errors = null;

        foreach (var kvp in _cache.ToArray())
        {
            if (kvp.Value.IsValueCreated && kvp.Value.Value is IDisposable d)
            {
                try
                {
                    d.Dispose();
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }
        }

        _cache.Clear();

        if (errors is { Count: > 0 })
            throw new AggregateException("One or more resilience pipelines threw during Dispose.", errors);
    }

    private void EvictAndDispose(string name)
    {
        if (_cache.TryRemove(name, out var old) && old.IsValueCreated && old.Value is IDisposable d)
            d.Dispose();
    }

    private IResiliencePipeline BuildFromOptions(ResiliencePolicyOptions policyOptions)
    {
        ValidateOptions(policyOptions);

        var builder = new ResiliencePipelineBuilder();

        if (policyOptions.RateLimiter is not null)
        {
            var logger = loggerFactory?.CreateLogger<RateLimiterStrategy>();
            builder.AddRateLimiter(policyOptions.RateLimiter, logger);
        }

        if (policyOptions.Timeout is not null)
        {
            var logger = loggerFactory?.CreateLogger<TimeoutStrategy>();
            builder.AddTimeout(policyOptions.Timeout, logger);
        }

        if (policyOptions.Hedging is not null)
        {
            var logger = loggerFactory?.CreateLogger<HedgingStrategy>();
            builder.AddHedging(policyOptions.Hedging, logger);
        }

        if (policyOptions.Bulkhead is not null)
        {
            var logger = loggerFactory?.CreateLogger<BulkheadStrategy>();
            builder.AddBulkhead(policyOptions.Bulkhead, logger);
        }

        if (policyOptions.CircuitBreaker is not null)
        {
            var logger = loggerFactory?.CreateLogger<CircuitBreakerStrategy>();
            builder.AddCircuitBreaker(stateStore, policyOptions.CircuitBreaker, logger);
        }

        if (policyOptions.Retry is not null)
        {
            var logger = loggerFactory?.CreateLogger<RetryStrategy>();
            builder.AddRetry(policyOptions.Retry, logger);
        }

        return builder.Build();
    }

    private static void ValidateOptions(ResiliencePolicyOptions policyOptions)
    {
        if (policyOptions.Retry is { } retry)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(retry.MaxRetries, nameof(retry.MaxRetries));
        }

        if (policyOptions.CircuitBreaker is { } cb)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(cb.FailureThreshold, 1, nameof(cb.FailureThreshold));
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cb.BreakDuration, TimeSpan.Zero, nameof(cb.BreakDuration));
        }

        if (policyOptions.Bulkhead is { } bh)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(bh.MaxConcurrency, 1, nameof(bh.MaxConcurrency));
            ArgumentOutOfRangeException.ThrowIfNegative(bh.MaxQueuedActions, nameof(bh.MaxQueuedActions));
        }

        if (policyOptions.Timeout is { } to)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(to.Timeout, TimeSpan.Zero, nameof(to.Timeout));
        }

        if (policyOptions.Hedging is { } hg)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(hg.MaxAttempts, 1, nameof(hg.MaxAttempts));
        }

        if (policyOptions.RateLimiter is { } rl)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(rl.MaxRequests, 1, nameof(rl.MaxRequests));
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(rl.Window, TimeSpan.Zero, nameof(rl.Window));
        }
    }
}
