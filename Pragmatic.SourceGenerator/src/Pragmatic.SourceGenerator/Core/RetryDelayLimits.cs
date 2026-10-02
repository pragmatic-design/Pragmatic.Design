namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The ceiling a generated retry delay is truncated at, mirrored from the runtime.
/// </summary>
/// <remarks>
///     <para>
///         Both engines that read <c>[Retry]</c> stop growing here: a job reschedules no later than
///         this, and a handler waits no longer. Without it a declaration of twenty attempts with a
///         one-second base asks for a wait measured in days — arithmetic that never overflows and is
///         still nobody's intent.
///     </para>
///     <para>
///         ⚠️ Hand-written copy of <c>Pragmatic.Jobs.JobRetryBackoff.Max</c>. The generator compiles to
///         netstandard2.0 and references no runtime assembly, so it cannot name the constant it has to
///         emit; the compiler cannot check that these two agree. Change one, change the other in the
///         same commit.
///     </para>
/// </remarks>
internal static class RetryDelayLimits
{
    /// <summary>Thirty minutes, in milliseconds.</summary>
    public const int MaxDelayMilliseconds = 30 * 60 * 1000;
}
