namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Options for the fallback strategy.
/// </summary>
public sealed class FallbackOptions<TResult>
{
    /// <summary>Factory that produces the fallback value.</summary>
    public required Func<Exception, ResilienceContext, CancellationToken, Task<TResult>> FallbackAction { get; set; }

    /// <summary>Optional predicate: which exceptions trigger the fallback.</summary>
    public Func<Exception, bool>? ShouldHandle { get; set; }

    /// <summary>Optional callback invoked when fallback is used.</summary>
    public Action<Exception, ResilienceContext>? OnFallback { get; set; }
}
