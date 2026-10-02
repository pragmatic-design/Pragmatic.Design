using Microsoft.Extensions.Logging;
using Pragmatic.Resilience.Diagnostics;
using Pragmatic.Resilience.Telemetry;

namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Bulkhead (concurrency limiter) strategy. Limits concurrent executions
/// to prevent one operation from consuming all resources.
/// </summary>
public sealed class BulkheadStrategy : IResilienceStrategy, IDisposable
{
    private readonly BulkheadOptions _options;
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger? _logger;
    private int _queueLength;

    public BulkheadStrategy(BulkheadOptions options, ILogger? logger = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxQueuedActions);
        _options = options;
        _semaphore = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        _logger = logger;
    }

    /// <summary>Order 200 — outer strategy, limits concurrency before CB/retry.</summary>
    public int Order => StrategyOrder.Bulkhead;

    /// <summary>Current number of available execution slots.</summary>
    public int AvailableSlots => _semaphore.CurrentCount;

    /// <summary>Number of callers currently waiting for a slot (bounded by <see cref="BulkheadOptions.MaxQueuedActions"/>).</summary>
    public int QueuedCount => Volatile.Read(ref _queueLength);

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> next,
        ResilienceContext context,
        CancellationToken ct)
    {
        // Fast path: take a slot without waiting if one is free.
        var acquired = await _semaphore.WaitAsync(0, ct).ConfigureAwait(false);

        if (!acquired)
        {
            // No free slot — try to enter the bounded queue. The queue admits at most
            // MaxQueuedActions waiters; excess callers (and any caller when queuing is disabled,
            // i.e. MaxQueuedActions == 0 or QueueTimeout <= 0) are rejected immediately.
            var position = Interlocked.Increment(ref _queueLength);
            try
            {
                if (position > _options.MaxQueuedActions || _options.QueueTimeout <= TimeSpan.Zero)
                    RejectAndThrow(context);

                acquired = await _semaphore.WaitAsync(_options.QueueTimeout, ct).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _queueLength);
            }

            if (!acquired)
                RejectAndThrow(context);
        }

        try
        {
            return await next(context, ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private void RejectAndThrow(ResilienceContext context)
    {
        ResilienceDiagnostics.BulkheadRejections.Add(1,
            new KeyValuePair<string, object?>("operation.name", context.OperationName));

        if (_logger is not null)
            ResilienceLogMessages.LogBulkheadRejected(_logger, context.OperationName, _options.MaxConcurrency);

        throw new BulkheadRejectedException(context.OperationName, _options.MaxConcurrency);
    }

    public void Dispose() => _semaphore.Dispose();
}
