using Microsoft.Extensions.Logging;
using Pragmatic.Resilience.Diagnostics;
using Pragmatic.Resilience.Telemetry;

namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Retry resilience strategy with configurable backoff, jitter, and retry predicate.
/// Uses proportional jitter (delay multiplied by a random factor in [0.5, 1.5)) for thundering herd prevention.
/// </summary>
public sealed class RetryStrategy(RetryOptions options, ILogger? logger = null) : IResilienceStrategy
{
    /// <summary>Order 400 — inner strategy, close to the operation.</summary>
    public int Order => StrategyOrder.Retry;

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> next,
        ResilienceContext context,
        CancellationToken ct)
    {
        if (options.MaxRetries <= 0)
            return await next(context, ct).ConfigureAwait(false);

        Exception? lastException = null;

        for (var attempt = 0; attempt <= options.MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            context.AttemptNumber = attempt;

            try
            {
                return await next(context, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // Don't retry on external cancellation
            }
            catch (Exception ex)
            {
                lastException = ex;

                if (options.ShouldRetry is not null && !options.ShouldRetry(ex))
                {
                    if (logger is not null)
                        ResilienceLogMessages.LogRetrySkipped(logger, context.OperationName, ex.Message);
                    throw;
                }

                if (attempt >= options.MaxRetries)
                    break; // All retries exhausted

                var delay = CalculateDelay(attempt);

                ResilienceDiagnostics.RetryAttempts.Add(1,
                    new KeyValuePair<string, object?>("operation.name", context.OperationName));

                if (logger is not null)
                {
                    ResilienceLogMessages.LogRetryAttempt(
                        logger, attempt + 1, options.MaxRetries,
                        context.OperationName, delay.TotalMilliseconds, ex.Message);
                }

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        if (logger is not null)
        {
            ResilienceLogMessages.LogRetryExhausted(
                logger, options.MaxRetries,
                context.OperationName, lastException?.Message ?? "unknown");
        }

        throw new RetryExhaustedException(
            context.OperationName,
            options.MaxRetries,
            lastException);
    }

    internal TimeSpan CalculateDelay(int attempt)
    {
        var baseMs = options.BaseDelay.TotalMilliseconds;

        var delayMs = options.BackoffType switch
        {
            BackoffType.Constant => baseMs,
            BackoffType.Linear => baseMs * (attempt + 1),
            // Cap exponent at 30 to limit the multiplier; also clamp the product to avoid double overflow
            // when baseMs itself is very large (e.g. TimeSpan.MaxValue.TotalMilliseconds).
            BackoffType.Exponential => Math.Min(baseMs * Math.Pow(2, Math.Min(attempt, 30)), double.MaxValue / 2),
            _ => baseMs
        };

        if (options.UseJitter)
        {
            // Proportional jitter: delay * random factor in [0.5, 1.5)
            delayMs *= 0.5 + Random.Shared.NextDouble();
        }

        var maxMs = options.MaxDelay.TotalMilliseconds;
        delayMs = Math.Min(delayMs, maxMs);

        return TimeSpan.FromMilliseconds(delayMs);
    }
}
