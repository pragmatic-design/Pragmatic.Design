namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Options for the hedging (parallel execution, first-wins) strategy.
/// </summary>
public sealed class HedgingOptions
{
    /// <summary>Delay before launching each subsequent hedged attempt. Default: 2 seconds.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Maximum number of parallel attempts (including the first). Default: 2.</summary>
    public int MaxAttempts { get; set; } = 2;
}
