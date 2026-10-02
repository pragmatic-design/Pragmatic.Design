namespace Pragmatic.Jobs.Attributes;

/// <summary>
///     Marks a class as a background job for SG discovery.
///     Used for delayed/fire-and-forget jobs (not recurring).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JobAttribute : Attribute
{
    /// <summary>
    ///     Scheduling priority: due jobs with a higher value are picked up before lower ones,
    ///     ties broken by scheduled time. Default: 0.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    ///     Maximum instances of this job type a single host runs at once. 0 (default) means only the
    ///     global worker count bounds it. Use to stop one heavy job type from starving the pool.
    /// </summary>
    public int MaxConcurrency { get; set; }
}
