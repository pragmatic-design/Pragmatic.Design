namespace Pragmatic.Logging.Tests;

/// <summary>What a piece of work allocates on the calling thread, measured so that a cost per call cannot hide.</summary>
public static class ThreadAllocations
{
    /// <summary>
    ///     Runs <paramref name="batch" /> once to warm it, then five times measured: the fewest bytes one run allocated,
    ///     and every sample as text for a failure message.
    /// </summary>
    /// <remarks>
    ///     A byte the work costs is in every run, the smallest included. The minimum leaves out what the runtime
    ///     allocates on the thread only now and then: under the gate's load, a single run of a path that allocates
    ///     nothing has measured 12,360 B, the same figure on two different paths of the JSON provider, about once in
    ///     forty runs of the suite and never in a run alone (#288). What allocates it was looked for and not found:
    ///     it is not a multiple of a line, and no allocation trace of a failing run was taken.
    /// </remarks>
    public static (long Fewest, string Samples) Fewest(Action batch)
    {
        batch();

        var samples = new long[5];
        for (var run = 0; run < samples.Length; run++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            batch();
            samples[run] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return (samples.Min(), string.Join(", ", samples));
    }
}
