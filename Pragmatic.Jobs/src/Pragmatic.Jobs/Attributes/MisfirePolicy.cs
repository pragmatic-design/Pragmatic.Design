namespace Pragmatic.Jobs.Attributes;

/// <summary>
///     What to do with a recurring occurrence the scheduler missed because the host was down or
///     saturated past the misfire threshold.
/// </summary>
public enum MisfirePolicy
{
    /// <summary>
    ///     Enqueue the missed occurrence once, then resume from the next future occurrence. Intermediate
    ///     occurrences that were also missed are not replayed. This is the default.
    /// </summary>
    RunOnce = 0,

    /// <summary>
    ///     Do not run any missed occurrence — advance straight to the next future occurrence. Use for
    ///     jobs where a late run is worse than a skipped one (e.g. a "send the 9am digest" job that
    ///     must not fire at noon).
    /// </summary>
    Skip = 1
}
