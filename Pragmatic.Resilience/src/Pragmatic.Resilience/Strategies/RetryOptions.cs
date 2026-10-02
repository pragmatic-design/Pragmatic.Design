using System.ComponentModel.DataAnnotations;

namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Configuration for the retry resilience strategy.
/// </summary>
public sealed class RetryOptions
{
    /// <summary>Maximum retry attempts (default: 3). 0 = no retries.</summary>
    [Range(0, 100)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay between retries (default: 200ms).</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Backoff type (default: Exponential).</summary>
    public BackoffType BackoffType { get; set; } = BackoffType.Exponential;

    /// <summary>Maximum delay cap (default: 30s). Prevents exponential from growing unbounded.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Add jitter to delays to prevent thundering herd (default: true).</summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Predicate to determine if an exception should trigger retry.
    /// If null, all exceptions trigger retry.
    /// </summary>
    public Func<Exception, bool>? ShouldRetry { get; set; }
}
