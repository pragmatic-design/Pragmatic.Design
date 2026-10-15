namespace Pragmatic.Persistence.Benchmarks.Workload;

/// <summary>
///     Counts EF Core's <c>DetectChangesStarting</c> events: one per change-detection pass over the tracker,
///     whoever asked for it.
/// </summary>
public sealed class DetectChangesCounter
{
    public int Passes { get; private set; }

    public void Log(string message) => Passes++;
}
