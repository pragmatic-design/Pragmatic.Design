using Microsoft.Extensions.Logging;
using Pragmatic.Resilience.Telemetry;

namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Fallback strategy. Catches exceptions and provides an alternative result.
/// Outermost strategy — the last resort: it wraps the entire pipeline so retry, circuit
/// breaker, hedging etc. observe failures first, and only what escapes them falls back.
/// </summary>
public sealed class FallbackStrategy<TResult>(FallbackOptions<TResult> options, ILogger? logger = null)
    : IResilienceStrategy
{
    /// <summary>Order 25 — outermost, last resort (see <see cref="StrategyOrder.Fallback"/>).</summary>
    public int Order => StrategyOrder.Fallback;

    async Task<T> IResilienceStrategy.ExecuteAsync<T>(
        Func<ResilienceContext, CancellationToken, Task<T>> next,
        ResilienceContext context,
        CancellationToken ct)
    {
        // Fallback only applies when TResult matches T
        if (typeof(T) != typeof(TResult))
            return await next(context, ct).ConfigureAwait(false);

        try
        {
            return await next(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Don't fallback on external cancellation
        }
        catch (Exception ex)
        {
            if (options.ShouldHandle is not null && !options.ShouldHandle(ex))
                throw;

            if (logger is not null)
                ResilienceLogMessages.LogFallbackUsed(logger, context.OperationName, ex.Message);

            options.OnFallback?.Invoke(ex, context);

            var fallbackResult = await options.FallbackAction(ex, context, ct).ConfigureAwait(false);
            // Safe conversion: typeof(T) == typeof(TResult) is guaranteed by the guard at the top of ExecuteAsync.
            // Use explicit type-check to produce a clear error if the invariant is somehow violated.
            if (fallbackResult is T typed)
                return typed;
            throw new InvalidCastException($"FallbackAction returned {fallbackResult?.GetType().Name ?? "null"} but expected {typeof(T).Name}.");
        }
    }
}
