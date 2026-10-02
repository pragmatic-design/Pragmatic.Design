namespace Pragmatic.Resilience.Attributes;

/// <summary>
///     Declares that repeated failures of the decorated <c>[MessageHandler]</c> stop further attempts for
///     a while. Only the messaging engine reads it: on a job or any other class it does nothing
///     (PRAG0464); a domain action takes <c>[ResiliencePolicy]</c>.
/// </summary>
/// <remarks>
///     ⚠️ Leave a property out and the engine that reads the declaration applies its own.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CircuitBreakerAttribute : Attribute
{
    /// <summary>
    ///     Consecutive failures before the circuit opens. Leave it out and the engine decides.
    /// </summary>
    public int FailureThreshold { get; set; }

    /// <summary>
    ///     How long the circuit stays open, in seconds, before a probe is allowed through.
    ///     Leave it out and the engine decides.
    /// </summary>
    public int BreakDurationSeconds { get; set; }
}
