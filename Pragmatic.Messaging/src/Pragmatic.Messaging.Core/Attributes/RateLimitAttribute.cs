namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Throttles how many messages this handler processes per time window (per process). The
///     SG-generated pipeline counts executions in a fixed window and delays past-limit messages
///     to the next window — useful when the handler calls a rate-limited downstream API.
/// </summary>
/// <param name="permitsPerPeriod">Executions allowed per period. Must be positive.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RateLimitAttribute(int permitsPerPeriod) : Attribute
{
    /// <summary>Executions allowed per period.</summary>
    public int PermitsPerPeriod { get; } = permitsPerPeriod;

    /// <summary>Window length in seconds. Default: 1.</summary>
    public int PeriodSeconds { get; set; } = 1;
}
