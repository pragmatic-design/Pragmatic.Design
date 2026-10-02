namespace Pragmatic.Resilience.Pipeline;

/// <summary>
/// No-op resilience pipeline. Executes the operation directly without any strategy.
/// Used as default when no resilience is configured (zero overhead).
/// </summary>
public sealed class PassthroughPipeline : IResiliencePipeline
{
    public static readonly PassthroughPipeline Instance = new();

    public Task<TResult> ExecuteAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> operation,
        ResilienceContext context,
        CancellationToken ct = default)
        => operation(context, ct);

    public Task ExecuteAsync(
        Func<ResilienceContext, CancellationToken, Task> operation,
        ResilienceContext context,
        CancellationToken ct = default)
        => operation(context, ct);
}
