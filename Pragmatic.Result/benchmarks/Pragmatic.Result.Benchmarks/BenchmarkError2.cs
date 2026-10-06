namespace Pragmatic.Result.Benchmarks;

/// <summary>The second error of the multi-error result.</summary>
public sealed record BenchmarkError2 : Error
{
    public static readonly BenchmarkError2 Instance = new();

    private BenchmarkError2()
    {
    }

    public string Message { get; } = "Benchmark error 2";
    public override string Code => "BENCH2";
    public override int StatusCode => 400;
}
