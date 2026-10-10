using BenchmarkDotNet.Attributes;

namespace Pragmatic.Result.Benchmarks;

/// <summary>
///     The floor for each row: the same work written by hand without the types, as the code a <c>Result</c>
///     replaces would write it (#132).
/// </summary>
/// <remarks>
///     <para>
///         A <c>Result&lt;int, E&gt;</c> is a value tuple <c>(int Value, E? Error)</c>, a <c>VoidResult&lt;E&gt;</c>
///         the error alone, a <c>Maybe&lt;int&gt;</c> <c>(bool HasValue, int Value)</c>, a two-error result
///         <c>(int Value, E1? A, E2? B)</c>. <c>Match</c>, <c>Map</c> and <c>Bind</c> become the branch they stand
///         for: hand-written code does not pass itself a delegate.
///     </para>
///     <para>
///         The explicit and implicit creations do the same work and share a twin; so do the two ways of reading a
///         value out of a result that may have failed. A row's overhead is its mean over its twin's, in the same
///         run.
///     </para>
/// </remarks>
public partial class ResultBenchmarks
{
    private (int Value, BenchmarkError? Error)[] _tuples = null!;
    private (int Value, BenchmarkError? Error)[] _successTuples = null!;
    private (bool HasValue, int Value)[] _maybeTuples = null!;
    private (int Value, BenchmarkError? A, BenchmarkError2? B)[] _multiTuples = null!;

    private (int Value, BenchmarkError? Error)[] _tupleSink = null!;
    private (string? Value, BenchmarkError? Error)[] _stringTupleSink = null!;
    private BenchmarkError?[] _errorSink = null!;
    private (bool HasValue, int Value)[] _maybeTupleSink = null!;
    private (int Value, BenchmarkError? A, BenchmarkError2? B)[] _multiTupleSink = null!;

    private void SetupTwins()
    {
        _tuples = new (int, BenchmarkError?)[Batch];
        _successTuples = new (int, BenchmarkError?)[Batch];
        _maybeTuples = new (bool, int)[Batch];
        _multiTuples = new (int, BenchmarkError?, BenchmarkError2?)[Batch];

        for (var i = 0; i < Batch; i++)
        {
            var value = _values[i];
            var fails = Fails(i);
            _successTuples[i] = (value, null);
            _tuples[i] = fails ? (0, BenchmarkError.Instance) : (value, null);
            _maybeTuples[i] = fails ? (false, 0) : (true, value);
            _multiTuples[i] = (i % 8) switch
            {
                3 => (0, BenchmarkError.Instance, null),
                7 => (0, null, BenchmarkError2.Instance),
                _ => (value, null, null),
            };
        }

        _tupleSink = new (int, BenchmarkError?)[Batch];
        _stringTupleSink = new (string?, BenchmarkError?)[Batch];
        _errorSink = new BenchmarkError?[Batch];
        _maybeTupleSink = new (bool, int)[Batch];
        _multiTupleSink = new (int, BenchmarkError?, BenchmarkError2?)[Batch];
    }

    /// <summary>Twin of <see cref="CreateSuccess" /> and <see cref="CreateSuccess_Implicit" />.</summary>
    [Benchmark]
    public void Twin_CreateSuccess()
    {
        for (var i = 0; i < Batch; i++)
            _tupleSink[i] = (_values[i], null);
    }

    /// <summary>Twin of <see cref="CreateFailure" /> and <see cref="CreateFailure_Implicit" />.</summary>
    [Benchmark]
    public void Twin_CreateFailure()
    {
        for (var i = 0; i < Batch; i++)
            _tupleSink[i] = (0, BenchmarkError.Instance);
    }

    /// <summary>Twin of <see cref="IsSuccess_Check" />.</summary>
    [Benchmark]
    public int Twin_IsSuccess()
    {
        var successes = 0;
        for (var i = 0; i < Batch; i++)
            if (_tuples[i].Error is null)
                successes++;
        return successes;
    }

    /// <summary>Twin of <see cref="Value_DirectAccess" />.</summary>
    [Benchmark]
    public int Twin_Value()
    {
        var sum = 0;
        for (var i = 0; i < Batch; i++)
            sum += _successTuples[i].Value;
        return sum;
    }

    /// <summary>Twin of <see cref="TryGetValue_Pattern" /> and <see cref="Match_Pattern" />.</summary>
    [Benchmark]
    public int Twin_ValueOrZero()
    {
        var sum = 0;
        for (var i = 0; i < Batch; i++)
        {
            var result = _tuples[i];
            sum += result.Error is null ? result.Value : 0;
        }

        return sum;
    }

    /// <summary>Twin of <see cref="Map_SingleTransform" />.</summary>
    [Benchmark]
    public void Twin_Map()
    {
        for (var i = 0; i < Batch; i++)
        {
            var result = _tuples[i];
            _stringTupleSink[i] = result.Error is null ? (result.Value.ToString(), null) : (null, result.Error);
        }
    }

    /// <summary>Twin of <see cref="Map_ChainedTransforms" />.</summary>
    [Benchmark]
    public void Twin_MapChained()
    {
        for (var i = 0; i < Batch; i++)
        {
            var result = _tuples[i];
            _stringTupleSink[i] = result.Error is null ? ((result.Value * 2 + 10).ToString(), null) : (null, result.Error);
        }
    }

    /// <summary>Twin of <see cref="Bind_SingleOperation" />.</summary>
    [Benchmark]
    public void Twin_Bind()
    {
        for (var i = 0; i < Batch; i++)
        {
            var result = _tuples[i];
            _tupleSink[i] = result.Error is null ? (result.Value * 2, null) : (0, result.Error);
        }
    }

    /// <summary>Twin of <see cref="VoidResult_Success" />.</summary>
    [Benchmark]
    public void Twin_VoidSuccess()
    {
        for (var i = 0; i < Batch; i++)
            _errorSink[i] = null;
    }

    /// <summary>Twin of <see cref="VoidResult_Failure" />.</summary>
    [Benchmark]
    public void Twin_VoidFailure()
    {
        for (var i = 0; i < Batch; i++)
            _errorSink[i] = BenchmarkError.Instance;
    }

    /// <summary>Twin of <see cref="Maybe_CreateSome" />.</summary>
    [Benchmark]
    public void Twin_MaybeSome()
    {
        for (var i = 0; i < Batch; i++)
            _maybeTupleSink[i] = (true, _values[i]);
    }

    /// <summary>Twin of <see cref="Maybe_CreateNone" />.</summary>
    [Benchmark]
    public void Twin_MaybeNone()
    {
        for (var i = 0; i < Batch; i++)
            _maybeTupleSink[i] = (false, 0);
    }

    /// <summary>Twin of <see cref="Maybe_GetValueOrDefault" /> and <see cref="Maybe_Match" />.</summary>
    [Benchmark]
    public int Twin_MaybeValueOrZero()
    {
        var sum = 0;
        for (var i = 0; i < Batch; i++)
        {
            var maybe = _maybeTuples[i];
            sum += maybe.HasValue ? maybe.Value : 0;
        }

        return sum;
    }

    /// <summary>Twin of <see cref="MultiError_CreateSuccess" />.</summary>
    [Benchmark]
    public void Twin_MultiSuccess()
    {
        for (var i = 0; i < Batch; i++)
            _multiTupleSink[i] = (_values[i], null, null);
    }

    /// <summary>Twin of <see cref="MultiError_Match" />.</summary>
    [Benchmark]
    public int Twin_MultiMatch()
    {
        var sum = 0;
        for (var i = 0; i < Batch; i++)
        {
            var result = _multiTuples[i];
            sum += result.A is not null ? -1 : result.B is not null ? -2 : result.Value;
        }

        return sum;
    }
}
