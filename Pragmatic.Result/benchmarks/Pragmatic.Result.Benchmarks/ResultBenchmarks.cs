// =============================================================================
// Result Benchmarks
// Measures performance of Result operations vs traditional approaches
// =============================================================================

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;

namespace Pragmatic.Result.Benchmarks;

[Config(typeof(Config))]
[MemoryDiagnoser]
// No explicit RuntimeMoniker: the job runs against the host process runtime, which is the project's
// TargetFramework (net10.0). This tracks the module's target automatically instead of a pinned moniker.
[SimpleJob]
public class ResultBenchmarks
{
    // =========================================================================
    // Access Pattern Benchmarks
    // =========================================================================

    private static readonly Result<int, BenchmarkError> SuccessResult =
        Result<int, BenchmarkError>.Success(42);

    private static readonly Result<int, BenchmarkError> FailureResult =
        Result<int, BenchmarkError>.Failure(BenchmarkError.Instance);

    // =========================================================================
    // Maybe Benchmarks
    // =========================================================================

    private static readonly Maybe<int> SomeMaybe = Maybe<int>.Some(42);
    private static readonly Maybe<int> NoneMaybe = Maybe<int>.None();

    // =========================================================================
    // Multi-Error Result Benchmarks
    // =========================================================================

    private static readonly Result<int, BenchmarkError, BenchmarkError2> MultiSuccess =
        Result<int, BenchmarkError, BenchmarkError2>.Success(42);

    // =========================================================================
    // Result Creation Benchmarks
    // =========================================================================

    [Benchmark(Baseline = true)]
    public Result<int, BenchmarkError> CreateSuccess()
    {
        return Result<int, BenchmarkError>.Success(42);
    }

    [Benchmark]
    public Result<int, BenchmarkError> CreateFailure()
    {
        return Result<int, BenchmarkError>.Failure(BenchmarkError.Instance);
    }

    [Benchmark]
    public Result<int, BenchmarkError> CreateSuccess_Implicit()
    {
        return 42;
    }

    [Benchmark]
    public Result<int, BenchmarkError> CreateFailure_Implicit()
    {
        return BenchmarkError.Instance;
    }

    [Benchmark]
    public bool IsSuccess_Check()
    {
        return SuccessResult.IsSuccess;
    }

    [Benchmark]
    public int Value_DirectAccess()
    {
        return SuccessResult.Value;
    }

    [Benchmark]
    public int TryGetValue_Pattern()
    {
        return SuccessResult.TryGetValue(out var value) ? value : 0;
    }

    [Benchmark]
    public int Match_Pattern()
    {
        return SuccessResult.Match(v => v, e => 0);
    }

    // =========================================================================
    // Map/Bind Chain Benchmarks
    // =========================================================================

    [Benchmark]
    public Result<string, BenchmarkError> Map_SingleTransform()
    {
        return SuccessResult.Map(v => v.ToString());
    }

    [Benchmark]
    public Result<string, BenchmarkError> Map_ChainedTransforms()
    {
        return SuccessResult
            .Map(v => v * 2)
            .Map(v => v + 10)
            .Map(v => v.ToString());
    }

    [Benchmark]
    public Result<int, BenchmarkError> Bind_SingleOperation()
    {
        return SuccessResult.Bind(v =>
            Result<int, BenchmarkError>.Success(v * 2));
    }

    // =========================================================================
    // VoidResult Benchmarks
    // =========================================================================

    [Benchmark]
    public VoidResult<BenchmarkError> VoidResult_Success()
    {
        return VoidResult<BenchmarkError>.Success();
    }

    [Benchmark]
    public VoidResult<BenchmarkError> VoidResult_Failure()
    {
        return VoidResult<BenchmarkError>.Failure(BenchmarkError.Instance);
    }

    [Benchmark]
    public Maybe<int> Maybe_CreateSome()
    {
        return Maybe<int>.Some(42);
    }

    [Benchmark]
    public Maybe<int> Maybe_CreateNone()
    {
        return Maybe<int>.None();
    }

    [Benchmark]
    public int Maybe_GetValueOrDefault()
    {
        return SomeMaybe.GetValueOrDefault(0);
    }

    [Benchmark]
    public int Maybe_Match()
    {
        return SomeMaybe.Match(v => v, () => 0);
    }

    [Benchmark]
    public Result<int, BenchmarkError, BenchmarkError2> MultiError_CreateSuccess()
    {
        return Result<int, BenchmarkError, BenchmarkError2>.Success(42);
    }

    [Benchmark]
    public int MultiError_Match()
    {
        return MultiSuccess.Match(
            v => v,
            e1 => 0,
            e2 => 0);
    }

    private class Config : ManualConfig
    {
        public Config()
        {
            AddColumn(StatisticColumn.Mean);
            AddColumn(StatisticColumn.Median);
            AddColumn(StatisticColumn.P95);
        }
    }
}

// Benchmark error types - singleton pattern for benchmarking
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
