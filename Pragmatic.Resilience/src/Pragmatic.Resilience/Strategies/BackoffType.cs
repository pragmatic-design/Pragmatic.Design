namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Backoff algorithm for retry delay calculation.
/// </summary>
public enum BackoffType
{
    /// <summary>Same delay between each retry.</summary>
    Constant,

    /// <summary>Delay increases linearly: baseDelay * attemptNumber.</summary>
    Linear,

    /// <summary>Delay doubles each attempt: baseDelay * 2^attemptNumber.</summary>
    Exponential
}
