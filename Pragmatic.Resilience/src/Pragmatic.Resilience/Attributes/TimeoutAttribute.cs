namespace Pragmatic.Resilience.Attributes;

/// <summary>
///     Declares how long the decorated <c>[Job]</c>, <c>[RecurringJob]</c> or <c>[MessageHandler]</c> is
///     allowed to run before it is cancelled. On any other class nothing reads it (PRAG0464); a domain
///     action takes <c>[ResiliencePolicy]</c>.
/// </summary>
/// <remarks>
///     ⚠️ Leave the property out and the engine that reads the declaration applies its own limit.
///     The two are far apart on purpose — a background job runs for minutes, a message handler for
///     seconds — so naming one of them here would quietly halve or multiply the other.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TimeoutAttribute : Attribute
{
    /// <summary>
    ///     The limit in seconds. Leave it out and the engine decides. Negative is refused.
    /// </summary>
    public int TimeoutSeconds
    {
        get;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "TimeoutSeconds cannot be negative.");

            field = value;
        }
    }
}
