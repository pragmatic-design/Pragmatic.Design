namespace Pragmatic.Resilience.Attributes;

/// <summary>
///     How the delay between retry attempts grows.
/// </summary>
/// <remarks>
///     The delay itself is computed by whichever engine reads the declaration — a job reschedules
///     durably, a message handler redelivers — so this enum names the shape of the curve and not the
///     numbers on it.
/// </remarks>
public enum BackoffStrategy
{
    /// <summary>
    ///     Not written on the declaration: the engine that reads it applies its own default.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This member exists so that «unwritten» is representable. An attribute property that is
    ///     not written carries the CLR default, and without a zero member that default would be a real
    ///     strategy — silently choosing one for every declaration that did not ask.
    /// </remarks>
    Unspecified = 0,

    /// <summary>Constant delay: the base delay, every time.</summary>
    Fixed = 1,

    /// <summary>Exponential: the base delay doubled for each attempt.</summary>
    Exponential = 2,

    /// <summary>Exponential with random jitter added, to spread a thundering herd.</summary>
    ExponentialWithJitter = 3
}
