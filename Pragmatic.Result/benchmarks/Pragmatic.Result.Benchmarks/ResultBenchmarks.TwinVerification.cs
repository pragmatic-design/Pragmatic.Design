namespace Pragmatic.Result.Benchmarks;

/// <summary>The check that every twin does the work of the rows it stands for, run before anything is timed.</summary>
/// <remarks>
///     Each twin is compared with its row's output, not with the arithmetic: a twin that does less than its row
///     would make the row's overhead look larger than it is. Sinks start filled with the opposite outcome, as in
///     <see cref="VerifyEachComputesWhatItSays" />.
/// </remarks>
public partial class ResultBenchmarks
{
    internal void VerifyEachTwinDoesItsRowsWork()
    {
        var failures = new List<string>();
        void Check(string twin, bool holds)
        {
            if (!holds)
                failures.Add(twin);
        }

        CreateSuccess();
        Array.Fill(_tupleSink, (-1, BenchmarkError.Instance));
        Twin_CreateSuccess();
        Check(nameof(Twin_CreateSuccess), AllResults(i => _tupleSink[i].Error is null && _tupleSink[i].Value == _resultSink[i].Value));

        Array.Fill(_tupleSink, (-1, (BenchmarkError?)null));
        Twin_CreateFailure();
        Check(nameof(Twin_CreateFailure), AllResults(i => ReferenceEquals(_tupleSink[i].Error, BenchmarkError.Instance)));

        Check(nameof(Twin_IsSuccess), Twin_IsSuccess() == IsSuccess_Check());
        Check(nameof(Twin_Value), Twin_Value() == Value_DirectAccess());
        Check(nameof(Twin_ValueOrZero), Twin_ValueOrZero() == TryGetValue_Pattern() && Twin_ValueOrZero() == Match_Pattern());

        Map_SingleTransform();
        Array.Fill(_stringTupleSink, ("unwritten", (BenchmarkError?)null));
        Twin_Map();
        Check(nameof(Twin_Map), AllResults(i => SameAs(_stringTupleSink[i], _stringSink[i])));

        Map_ChainedTransforms();
        Array.Fill(_stringTupleSink, ("unwritten", (BenchmarkError?)null));
        Twin_MapChained();
        Check(nameof(Twin_MapChained), AllResults(i => SameAs(_stringTupleSink[i], _stringSink[i])));

        Bind_SingleOperation();
        Array.Fill(_tupleSink, (-1, (BenchmarkError?)null));
        Twin_Bind();
        Check(nameof(Twin_Bind), AllResults(i => _resultSink[i].IsSuccess
            ? _tupleSink[i].Error is null && _tupleSink[i].Value == _resultSink[i].Value
            : ReferenceEquals(_tupleSink[i].Error, _resultSink[i].Error)));

        Array.Fill(_errorSink, BenchmarkError.Instance);
        Twin_VoidSuccess();
        Check(nameof(Twin_VoidSuccess), AllResults(i => _errorSink[i] is null));

        Array.Fill(_errorSink, null);
        Twin_VoidFailure();
        Check(nameof(Twin_VoidFailure), AllResults(i => ReferenceEquals(_errorSink[i], BenchmarkError.Instance)));

        Array.Fill(_maybeTupleSink, (false, -1));
        Twin_MaybeSome();
        Check(nameof(Twin_MaybeSome), AllResults(i => _maybeTupleSink[i] == (true, _values[i])));

        Array.Fill(_maybeTupleSink, (true, -1));
        Twin_MaybeNone();
        Check(nameof(Twin_MaybeNone), AllResults(i => !_maybeTupleSink[i].HasValue));

        Check(nameof(Twin_MaybeValueOrZero), Twin_MaybeValueOrZero() == Maybe_GetValueOrDefault() && Twin_MaybeValueOrZero() == Maybe_Match());

        Array.Fill(_multiTupleSink, (-1, BenchmarkError.Instance, (BenchmarkError2?)null));
        Twin_MultiSuccess();
        Check(nameof(Twin_MultiSuccess), AllResults(i => _multiTupleSink[i] == (_values[i], null, null)));

        Check(nameof(Twin_MultiMatch), Twin_MultiMatch() == MultiError_Match());

        if (failures.Count > 0)
            throw new InvalidOperationException(
                "These twins do not do the work of their rows, so the overhead read against them would be wrong: "
                + string.Join(", ", failures));
    }

    private static bool SameAs((string? Value, BenchmarkError? Error) twin, Result<string, BenchmarkError> row)
        => row.IsSuccess
            ? twin.Error is null && twin.Value == row.Value
            : twin.Value is null && ReferenceEquals(twin.Error, row.Error);
}
