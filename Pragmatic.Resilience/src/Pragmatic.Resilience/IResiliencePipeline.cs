namespace Pragmatic.Resilience;

/// <summary>
/// Generic resilience pipeline that wraps an operation with configured strategies.
/// Strategies compose by ascending <c>Order</c> (lower = more external), so the runtime nesting is
/// Fallback → RateLimiter → Timeout → Hedging → Bulkhead → CircuitBreaker → Retry → Execute.
/// </summary>
public interface IResiliencePipeline
{
    /// <summary>Executes an async operation through the resilience pipeline.</summary>
    Task<TResult> ExecuteAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> operation,
        ResilienceContext context,
        CancellationToken ct = default);

    /// <summary>Executes a void async operation through the resilience pipeline.</summary>
    Task ExecuteAsync(
        Func<ResilienceContext, CancellationToken, Task> operation,
        ResilienceContext context,
        CancellationToken ct = default);
}
