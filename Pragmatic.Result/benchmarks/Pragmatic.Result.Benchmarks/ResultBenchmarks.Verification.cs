namespace Pragmatic.Result.Benchmarks;

/// <summary>The check that every benchmark computes what its name says, run before anything is timed.</summary>
/// <remarks>
///     The expected values are worked out here with plain arithmetic over the inputs, not with the types
///     under test. A benchmark that stores is checked against destinations first filled with the opposite
///     outcome, so one that wrote nothing cannot pass by leaving them as they were.
/// </remarks>
public partial class ResultBenchmarks
{
    private static bool Fails(int i) => i % 4 == 3;

    internal void VerifyEachComputesWhatItSays()
    {
        var failures = new List<string>();
        void Check(string benchmark, bool holds)
        {
            if (!holds)
                failures.Add(benchmark);
        }

        var successCount = 0;
        var sumOfAll = 0;
        var sumOfSuccesses = 0;
        var multiSum = 0;
        for (var i = 0; i < Batch; i++)
        {
            sumOfAll += _values[i];
            if (!Fails(i))
            {
                successCount++;
                sumOfSuccesses += _values[i];
            }

            multiSum += (i % 8) switch { 3 => -1, 7 => -2, _ => _values[i] };
        }

        FillResults(Result<int, BenchmarkError>.Failure(BenchmarkError.Instance));
        CreateSuccess();
        Check(nameof(CreateSuccess), AllResults(i => _resultSink[i].IsSuccess && _resultSink[i].Value == _values[i]));

        FillResults(Result<int, BenchmarkError>.Success(-1));
        CreateFailure();
        Check(nameof(CreateFailure), AllResults(i => _resultSink[i].IsFailure && ReferenceEquals(_resultSink[i].Error, BenchmarkError.Instance)));

        FillResults(Result<int, BenchmarkError>.Failure(BenchmarkError.Instance));
        CreateSuccess_Implicit();
        Check(nameof(CreateSuccess_Implicit), AllResults(i => _resultSink[i].IsSuccess && _resultSink[i].Value == _values[i]));

        FillResults(Result<int, BenchmarkError>.Success(-1));
        CreateFailure_Implicit();
        Check(nameof(CreateFailure_Implicit), AllResults(i => _resultSink[i].IsFailure && ReferenceEquals(_resultSink[i].Error, BenchmarkError.Instance)));

        Check(nameof(IsSuccess_Check), IsSuccess_Check() == successCount);
        Check(nameof(Value_DirectAccess), Value_DirectAccess() == sumOfAll);
        Check(nameof(TryGetValue_Pattern), TryGetValue_Pattern() == sumOfSuccesses);
        Check(nameof(Match_Pattern), Match_Pattern() == sumOfSuccesses);

        Array.Fill(_stringSink, Result<string, BenchmarkError>.Success("unwritten"));
        Map_SingleTransform();
        Check(nameof(Map_SingleTransform), AllResults(i => Fails(i)
            ? _stringSink[i].IsFailure
            : _stringSink[i].IsSuccess && _stringSink[i].Value == _values[i].ToString()));

        Array.Fill(_stringSink, Result<string, BenchmarkError>.Success("unwritten"));
        Map_ChainedTransforms();
        Check(nameof(Map_ChainedTransforms), AllResults(i => Fails(i)
            ? _stringSink[i].IsFailure
            : _stringSink[i].IsSuccess && _stringSink[i].Value == (_values[i] * 2 + 10).ToString()));

        FillResults(Result<int, BenchmarkError>.Success(-1));
        Bind_SingleOperation();
        Check(nameof(Bind_SingleOperation), AllResults(i => Fails(i)
            ? _resultSink[i].IsFailure
            : _resultSink[i].IsSuccess && _resultSink[i].Value == _values[i] * 2));

        Array.Fill(_voidSink, VoidResult<BenchmarkError>.Failure(BenchmarkError.Instance));
        VoidResult_Success();
        Check(nameof(VoidResult_Success), AllResults(i => _voidSink[i].IsSuccess));

        Array.Fill(_voidSink, VoidResult<BenchmarkError>.Success());
        VoidResult_Failure();
        Check(nameof(VoidResult_Failure), AllResults(i => _voidSink[i].IsFailure));

        Array.Fill(_maybeSink, Maybe<int>.None());
        Maybe_CreateSome();
        Check(nameof(Maybe_CreateSome), AllResults(i => _maybeSink[i].HasValue && _maybeSink[i].GetValueOrDefault(0) == _values[i]));

        Array.Fill(_maybeSink, Maybe<int>.Some(-1));
        Maybe_CreateNone();
        Check(nameof(Maybe_CreateNone), AllResults(i => _maybeSink[i].IsNone));

        Check(nameof(Maybe_GetValueOrDefault), Maybe_GetValueOrDefault() == sumOfSuccesses);
        Check(nameof(Maybe_Match), Maybe_Match() == sumOfSuccesses);

        Array.Fill(_multiSink, Result<int, BenchmarkError, BenchmarkError2>.Failure(BenchmarkError.Instance));
        MultiError_CreateSuccess();
        Check(nameof(MultiError_CreateSuccess), AllResults(i => _multiSink[i].Match(v => v, _ => -1, _ => -2) == _values[i]));

        Check(nameof(MultiError_Match), MultiError_Match() == multiSum);

        if (failures.Count > 0)
            throw new InvalidOperationException(
                "These benchmarks do not compute what their names say, so their rows would measure something else: "
                + string.Join(", ", failures));
    }

    private void FillResults(Result<int, BenchmarkError> value) => Array.Fill(_resultSink, value);

    private static bool AllResults(Func<int, bool> holds)
    {
        for (var i = 0; i < Batch; i++)
            if (!holds(i))
                return false;
        return true;
    }
}
