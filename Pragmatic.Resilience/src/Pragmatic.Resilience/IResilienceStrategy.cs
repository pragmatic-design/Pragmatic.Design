namespace Pragmatic.Resilience;

/// <summary>
/// A single resilience strategy (retry, timeout, etc.) that can wrap an operation.
/// Strategies are composed into a pipeline, ordered by <see cref="Order"/>.
/// Lower order = more external (wraps more of the pipeline).
/// </summary>
public interface IResilienceStrategy
{
    /// <summary>Strategy execution order. Lower = more external.</summary>
    int Order { get; }

    /// <summary>Executes the operation with this strategy applied.</summary>
    Task<TResult> ExecuteAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> next,
        ResilienceContext context,
        CancellationToken ct);
}
