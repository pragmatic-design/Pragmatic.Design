using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;

namespace Pragmatic.Result.Benchmarks;

/// <summary>
///     What the Result, VoidResult, Maybe and multi-error operations cost, each over a batch of
///     <see cref="Batch" /> inputs.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Per batch, not per operation.</b> These operations are a few instructions each, and measured
///         one at a time on inputs the JIT can see they came out at 0.0000 ns: <c>static readonly</c> fields
///         built from constants are constants after tier-1, so the work was folded away and the timer
///         measured an empty method. Here every benchmark walks arrays built in <see cref="Setup" />, which
///         the JIT cannot fold, and writes its results to a field or returns their sum, which it cannot
///         drop. A row's mean is the cost of <see cref="Batch" /> operations.
///     </para>
///     <para>
///         <see cref="Setup" /> runs every benchmark once and stops the run unless each computed what it
///         says (<see cref="VerifyEachComputesWhatItSays" />).
///     </para>
/// </remarks>
[Config(typeof(Config))]
[MemoryDiagnoser]
// No explicit RuntimeMoniker: the job runs against the host process runtime, which is the project's
// TargetFramework (net10.0). This tracks the module's target automatically instead of a pinned moniker.
[SimpleJob]
public partial class ResultBenchmarks
{
    /// <summary>How many inputs each benchmark walks per invocation.</summary>
    public const int Batch = 1024;

    // Inputs: every fourth one a failure, so a branch on the outcome cannot be predicted away entirely.
    private int[] _values = null!;
    private Result<int, BenchmarkError>[] _results = null!;
    private Result<int, BenchmarkError>[] _successes = null!;
    private Maybe<int>[] _maybes = null!;
    private Result<int, BenchmarkError, BenchmarkError2>[] _multi = null!;

    // Where the creating benchmarks put what they create: a store to the heap is not dead code.
    private Result<int, BenchmarkError>[] _resultSink = null!;
    private Result<string, BenchmarkError>[] _stringSink = null!;
    private VoidResult<BenchmarkError>[] _voidSink = null!;
    private Maybe<int>[] _maybeSink = null!;
    private Result<int, BenchmarkError, BenchmarkError2>[] _multiSink = null!;

    [GlobalSetup]
    public void Setup()
    {
        _values = new int[Batch];
        _results = new Result<int, BenchmarkError>[Batch];
        _successes = new Result<int, BenchmarkError>[Batch];
        _maybes = new Maybe<int>[Batch];
        _multi = new Result<int, BenchmarkError, BenchmarkError2>[Batch];

        for (var i = 0; i < Batch; i++)
        {
            var value = i * 7 % 1000 + 1;
            var fails = i % 4 == 3;
            _values[i] = value;
            _successes[i] = Result<int, BenchmarkError>.Success(value);
            _results[i] = fails
                ? Result<int, BenchmarkError>.Failure(BenchmarkError.Instance)
                : Result<int, BenchmarkError>.Success(value);
            _maybes[i] = fails ? Maybe<int>.None() : Maybe<int>.Some(value);
            _multi[i] = (i % 8) switch
            {
                3 => Result<int, BenchmarkError, BenchmarkError2>.Failure(BenchmarkError.Instance),
                7 => Result<int, BenchmarkError, BenchmarkError2>.Failure(BenchmarkError2.Instance),
                _ => Result<int, BenchmarkError, BenchmarkError2>.Success(value),
            };
        }

        _resultSink = new Result<int, BenchmarkError>[Batch];
        _stringSink = new Result<string, BenchmarkError>[Batch];
        _voidSink = new VoidResult<BenchmarkError>[Batch];
        _maybeSink = new Maybe<int>[Batch];
        _multiSink = new Result<int, BenchmarkError, BenchmarkError2>[Batch];

        VerifyEachComputesWhatItSays();
    }

    // ── Creation ──

    [Benchmark(Baseline = true)]
    public void CreateSuccess()
    {
        for (var i = 0; i < Batch; i++)
            _resultSink[i] = Result<int, BenchmarkError>.Success(_values[i]);
    }

    [Benchmark]
    public void CreateFailure()
    {
        for (var i = 0; i < Batch; i++)
            _resultSink[i] = Result<int, BenchmarkError>.Failure(BenchmarkError.Instance);
    }

    [Benchmark]
    public void CreateSuccess_Implicit()
    {
        for (var i = 0; i < Batch; i++)
            _resultSink[i] = _values[i];
    }

    [Benchmark]
    public void CreateFailure_Implicit()
    {
        for (var i = 0; i < Batch; i++)
            _resultSink[i] = BenchmarkError.Instance;
    }

    // ── Access ──

    [Benchmark]
    public int IsSuccess_Check()
    {
        var successes = 0;
        for (var i = 0; i < Batch; i++)
            if (_results[i].IsSuccess)
                successes++;
        return successes;
    }

    [Benchmark]
    public int Value_DirectAccess()
    {
        var sum = 0;
        for (var i = 0; i < Batch; i++)
            sum += _successes[i].Value;
        return sum;
    }

    [Benchmark]
    public int TryGetValue_Pattern()
    {
        var sum = 0;
        for (var i = 0; i < Batch; i++)
            sum += _results[i].TryGetValue(out var value) ? value : 0;
        return sum;
    }

    [Benchmark]
    public int Match_Pattern()
    {
        var sum = 0;
        for (var i = 0; i < Batch; i++)
            sum += _results[i].Match(v => v, _ => 0);
        return sum;
    }

    // ── Map and Bind ──

    [Benchmark]
    public void Map_SingleTransform()
    {
        for (var i = 0; i < Batch; i++)
            _stringSink[i] = _results[i].Map(v => v.ToString());
    }

    [Benchmark]
    public void Map_ChainedTransforms()
    {
        for (var i = 0; i < Batch; i++)
            _stringSink[i] = _results[i].Map(v => v * 2).Map(v => v + 10).Map(v => v.ToString());
    }

    [Benchmark]
    public void Bind_SingleOperation()
    {
        for (var i = 0; i < Batch; i++)
            _resultSink[i] = _results[i].Bind(v => Result<int, BenchmarkError>.Success(v * 2));
    }

    // ── VoidResult ──

    [Benchmark]
    public void VoidResult_Success()
    {
        for (var i = 0; i < Batch; i++)
            _voidSink[i] = VoidResult<BenchmarkError>.Success();
    }

    [Benchmark]
    public void VoidResult_Failure()
    {
        for (var i = 0; i < Batch; i++)
            _voidSink[i] = VoidResult<BenchmarkError>.Failure(BenchmarkError.Instance);
    }

    // ── Maybe ──

    [Benchmark]
    public void Maybe_CreateSome()
    {
        for (var i = 0; i < Batch; i++)
            _maybeSink[i] = Maybe<int>.Some(_values[i]);
    }

    [Benchmark]
    public void Maybe_CreateNone()
    {
        for (var i = 0; i < Batch; i++)
            _maybeSink[i] = Maybe<int>.None();
    }

    [Benchmark]
    public int Maybe_GetValueOrDefault()
    {
        var sum = 0;
        for (var i = 0; i < Batch; i++)
            sum += _maybes[i].GetValueOrDefault(0);
        return sum;
    }

    [Benchmark]
    public int Maybe_Match()
    {
        var sum = 0;
        for (var i = 0; i < Batch; i++)
            sum += _maybes[i].Match(v => v, () => 0);
        return sum;
    }

    // ── Multi-error ──

    [Benchmark]
    public void MultiError_CreateSuccess()
    {
        for (var i = 0; i < Batch; i++)
            _multiSink[i] = Result<int, BenchmarkError, BenchmarkError2>.Success(_values[i]);
    }

    [Benchmark]
    public int MultiError_Match()
    {
        var sum = 0;
        for (var i = 0; i < Batch; i++)
            sum += _multi[i].Match(v => v, _ => -1, _ => -2);
        return sum;
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
