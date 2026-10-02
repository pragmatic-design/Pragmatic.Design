namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Caps how many messages this handler processes concurrently (per process). The SG-generated
///     pipeline gates <c>HandleAsync</c> behind a static semaphore, so a slow handler cannot be
///     overwhelmed by a fast transport (prefetch/parallel consumers).
/// </summary>
/// <param name="maxConcurrent">Maximum concurrent executions. Must be positive.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ConcurrencyLimitAttribute(int maxConcurrent) : Attribute
{
    /// <summary>Maximum concurrent executions of the handler.</summary>
    public int MaxConcurrent { get; } = maxConcurrent;
}
