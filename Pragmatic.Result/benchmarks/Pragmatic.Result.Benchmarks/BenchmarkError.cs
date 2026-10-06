namespace Pragmatic.Result.Benchmarks;

/// <summary>The error the benchmarks fail with: one instance, so a failure allocates nothing of its own.</summary>
public sealed record BenchmarkError : Error
{
    public static readonly BenchmarkError Instance = new();

    private BenchmarkError()
    {
    }

    public string Message { get; } = "Benchmark error";
    public override string Code => "BENCH";
    public override int StatusCode => 400;
}
