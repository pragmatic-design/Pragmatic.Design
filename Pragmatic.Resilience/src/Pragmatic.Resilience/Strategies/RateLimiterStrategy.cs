using Microsoft.Extensions.Logging;
using Pragmatic.Resilience.Diagnostics;
using Pragmatic.Resilience.Telemetry;

namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Rate limiter strategy using a sliding window approach.
/// Rejects requests that exceed the configured rate within the time window.
/// </summary>
public sealed class RateLimiterStrategy : IResilienceStrategy, IDisposable
{
    private readonly RateLimiterOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Queue<DateTimeOffset> _timestamps = new();
    private readonly ILogger? _logger;

    public RateLimiterStrategy(RateLimiterOptions options, TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxRequests, 1);
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>Order 50 — outermost, rejects fast before any other strategy.</summary>
    public int Order => StrategyOrder.RateLimiter;

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> next,
        ResilienceContext context,
        CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();

            // Remove expired timestamps outside the window
            while (_timestamps.Count > 0 && now - _timestamps.Peek() > _options.Window)
                _timestamps.Dequeue();

            if (_timestamps.Count >= _options.MaxRequests)
            {
                // Cached tag avoids per-rejection heap allocation.
                var tag = new KeyValuePair<string, object?>("operation.name", context.OperationName);
                ResilienceDiagnostics.RateLimitRejections.Add(1, tag);

                if (_logger is not null)
                    ResilienceLogMessages.LogRateLimitRejected(_logger, context.OperationName, _options.MaxRequests, _options.Window.TotalSeconds);

                throw new RateLimitRejectedException(_options.MaxRequests, _options.Window);
            }

            _timestamps.Enqueue(now);
        }
        finally
        {
            _semaphore.Release();
        }

        return await next(context, ct).ConfigureAwait(false);
    }

    public void Dispose() => _semaphore.Dispose();
}
