// =============================================================================
// Result<TValue> (Untyped Error) Unit Tests
// =============================================================================

using Xunit;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     Tests for Result&lt;TValue&gt; which uses the base Error class instead of a typed error.
/// </summary>
public class ResultUntypedTests
{
    // =========================================================================
    // Factory Methods
    // =========================================================================

    [Fact]
    public void Success_CreatesSuccessResult()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Success_AllowsReferenceTypes()
    {
        var result = Result<string>.Success("hello");

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void Failure_CreatesFailureResult()
    {
        var result = Result<int>.Failure(new StringError("error"));

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.IsType<StringError>(result.Error);
    }

    [Fact]
    public void Failure_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Result<int>.Failure(null!));
    }

    [Fact]
    public void Failure_AcceptsAnyErrorSubtype()
    {
        var stringError = Result<int>.Failure(new StringError("msg"));
        var notFoundError = Result<int>.Failure(new TestNotFoundError("item"));
        var validationError = Result<int>.Failure(new TestValidationError("invalid"));

        Assert.IsType<StringError>(stringError.Error);
        Assert.IsType<TestNotFoundError>(notFoundError.Error);
        Assert.IsType<TestValidationError>(validationError.Error);
    }

    // =========================================================================
    // Property Access
    // =========================================================================

    [Fact]
    public void Value_ThrowsWhenFailure()
    {
        var result = Result<int>.Failure(new StringError("error"));

        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Error_ThrowsWhenSuccess()
    {
        var result = Result<int>.Success(42);

        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    [Fact]
    public void Error_ReturnsErrorWhenFailure()
    {
        var error = new StringError("test");
        var result = Result<int>.Failure(error);

        Assert.Same(error, result.Error);
    }

    // =========================================================================
    // TryGet Methods
    // =========================================================================

    [Fact]
    public void TryGetValue_ReturnsTrueWhenSuccess()
    {
        var result = Result<int>.Success(42);

        var success = result.TryGetValue(out var value);

        Assert.True(success);
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryGetValue_ReturnsFalseWhenFailure()
    {
        var result = Result<int>.Failure(new StringError("error"));

        var success = result.TryGetValue(out var value);

        Assert.False(success);
        Assert.Equal(default, value);
    }

    [Fact]
    public void TryGetError_ReturnsTrueWhenFailure()
    {
        var error = new StringError("error");
        var result = Result<int>.Failure(error);

        var success = result.TryGetError(out var outError);

        Assert.True(success);
        Assert.Same(error, outError);
    }

    [Fact]
    public void TryGetError_ReturnsFalseWhenSuccess()
    {
        var result = Result<int>.Success(42);

        var success = result.TryGetError(out var error);

        Assert.False(success);
        Assert.Null(error);
    }

    // =========================================================================
    // Match Methods
    // =========================================================================

    [Fact]
    public void Match_CallsOnSuccessWhenSuccess()
    {
        var result = Result<int>.Success(42);

        var output = result.Match(
            v => $"value: {v}",
            e => $"error: {e.Code}");

        Assert.Equal("value: 42", output);
    }

    [Fact]
    public void Match_CallsOnErrorWhenFailure()
    {
        var result = Result<int>.Failure(new StringError("error"));

        var output = result.Match(
            v => $"value: {v}",
            e => $"error: {e.Code}");

        Assert.Equal("error: TEST", output);
    }

    [Fact]
    public void MatchAction_CallsOnSuccessWhenSuccess()
    {
        var result = Result<int>.Success(42);
        var called = false;

        result.Match(
            v => called = true,
            e => called = false);

        Assert.True(called);
    }

    [Fact]
    public void MatchAction_CallsOnErrorWhenFailure()
    {
        var result = Result<int>.Failure(new StringError("error"));
        var called = false;

        result.Match(
            v => called = false,
            e => called = true);

        Assert.True(called);
    }

    [Fact]
    public void Match_ThrowsOnNullOnSuccess()
    {
        var result = Result<int>.Success(42);

        Assert.Throws<ArgumentNullException>(() =>
            result.Match(null!, e => "error"));
    }

    [Fact]
    public void Match_ThrowsOnNullOnError()
    {
        var result = Result<int>.Success(42);

        Assert.Throws<ArgumentNullException>(() =>
            result.Match(v => "success", null!));
    }

    // =========================================================================
    // Map Method
    // =========================================================================

    [Fact]
    public void Map_TransformsValueWhenSuccess()
    {
        var result = Result<int>.Success(42);

        var mapped = result.Map(v => v * 2);

        Assert.True(mapped.IsSuccess);
        Assert.Equal(84, mapped.Value);
    }

    [Fact]
    public void Map_PreservesErrorWhenFailure()
    {
        var error = new StringError("error");
        var result = Result<int>.Failure(error);

        var mapped = result.Map(v => v * 2);

        Assert.True(mapped.IsFailure);
        Assert.Same(error, mapped.Error);
    }

    [Fact]
    public void Map_CanChangeType()
    {
        var result = Result<int>.Success(42);

        var mapped = result.Map(v => $"number: {v}");

        Assert.True(mapped.IsSuccess);
        Assert.Equal("number: 42", mapped.Value);
    }

    [Fact]
    public void Map_ThrowsOnNullMapper()
    {
        var result = Result<int>.Success(42);

        Assert.Throws<ArgumentNullException>(() =>
            result.Map((Func<int, string>)null!));
    }

    // =========================================================================
    // Bind Method
    // =========================================================================

    [Fact]
    public void Bind_ChainsWhenSuccess()
    {
        var result = Result<int>.Success(42);

        var bound = result.Bind(v => Result<string>.Success($"value: {v}"));

        Assert.True(bound.IsSuccess);
        Assert.Equal("value: 42", bound.Value);
    }

    [Fact]
    public void Bind_PreservesErrorWhenFailure()
    {
        var error = new StringError("original error");
        var result = Result<int>.Failure(error);

        var bound = result.Bind(v => Result<string>.Success($"value: {v}"));

        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
    }

    [Fact]
    public void Bind_PropagatesNewError()
    {
        var result = Result<int>.Success(42);
        var newError = new StringError("new error");

        var bound = result.Bind(v => Result<string>.Failure(newError));

        Assert.True(bound.IsFailure);
        Assert.Same(newError, bound.Error);
    }

    [Fact]
    public void Bind_ThrowsOnNullBinder()
    {
        var result = Result<int>.Success(42);

        Assert.Throws<ArgumentNullException>(() =>
            result.Bind((Func<int, Result<string>>)null!));
    }

    // =========================================================================
    // Implicit Operators
    // =========================================================================

    [Fact]
    public void ImplicitOperator_FromValue()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ImplicitOperator_ToValue()
    {
        var result = Result<int>.Success(42);

        int value = result;

        Assert.Equal(42, value);
    }

    [Fact]
    public void ImplicitOperator_ToValue_ThrowsOnFailure()
    {
        var result = Result<int>.Failure(new StringError("error"));

        Assert.Throws<InvalidOperationException>(() =>
        {
            int _ = result;
        });
    }

    // =========================================================================
    // Deconstruction
    // =========================================================================

    [Fact]
    public void Deconstruct_Success()
    {
        var result = Result<int>.Success(42);

        var (isSuccess, value, error) = result;

        Assert.True(isSuccess);
        Assert.Equal(42, value);
        Assert.Null(error);
    }

    [Fact]
    public void Deconstruct_Failure()
    {
        var expectedError = new StringError("error");
        var result = Result<int>.Failure(expectedError);

        var (isSuccess, value, error) = result;

        Assert.False(isSuccess);
        Assert.Equal(default, value);
        Assert.Same(expectedError, error);
    }

    // =========================================================================
    // IResultBase Implementation
    // =========================================================================

    [Fact]
    public void IResultBase_ValueAsObject_ReturnsValueWhenSuccess()
    {
        var result = Result<int>.Success(42);
        IResultBase resultBase = result;

        Assert.Equal(42, resultBase.ValueAsObject);
    }

    [Fact]
    public void IResultBase_ValueAsObject_ReturnsNullWhenFailure()
    {
        var result = Result<int>.Failure(new StringError("error"));
        IResultBase resultBase = result;

        Assert.Null(resultBase.ValueAsObject);
    }

    [Fact]
    public void IResultBase_ErrorAsObject_ReturnsNullWhenSuccess()
    {
        var result = Result<int>.Success(42);
        IResultBase resultBase = result;

        Assert.Null(resultBase.ErrorAsObject);
    }

    [Fact]
    public void IResultBase_ErrorAsObject_ReturnsErrorWhenFailure()
    {
        var error = new StringError("error");
        var result = Result<int>.Failure(error);
        IResultBase resultBase = result;

        Assert.Same(error, resultBase.ErrorAsObject);
    }

    // =========================================================================
    // Edge Cases
    // =========================================================================

    [Fact]
    public void Result_WithComplexType_Works()
    {
        var complexValue = new ComplexTestType { Name = "test", Value = 42 };
        var result = Result<ComplexTestType>.Success(complexValue);

        Assert.True(result.IsSuccess);
        Assert.Same(complexValue, result.Value);
    }

    [Fact]
    public void Result_WithNullableValueType_Success()
    {
        var result = Result<int?>.Success(null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Result_ChainMapOperations()
    {
        var result = Result<int>.Success(5)
            .Map(x => x * 2)
            .Map(x => x + 1)
            .Map(x => x.ToString());

        Assert.True(result.IsSuccess);
        Assert.Equal("11", result.Value);
    }

    [Fact]
    public void Result_ChainBindOperations()
    {
        var result = Result<int>.Success(5)
            .Bind(x => x > 0
                ? Result<int>.Success(x * 2)
                : Result<int>.Failure(new StringError("must be positive")))
            .Bind(x => Result<string>.Success($"result: {x}"));

        Assert.True(result.IsSuccess);
        Assert.Equal("result: 10", result.Value);
    }

    [Fact]
    public void Result_MixedMapAndBind()
    {
        var result = Result<int>.Success(5)
            .Map(x => x * 2)
            .Bind(x => x > 5
                ? Result<int>.Success(x)
                : Result<int>.Failure(new StringError("too small")));

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value);
    }

    // =========================================================================
    // MatchAsync (#14 — symmetry with Result<T, TError>)
    // =========================================================================

    [Fact]
    public async Task MatchAsync_Func_Success_RunsOnSuccess()
    {
        var result = Result<int>.Success(21);

        var doubled = await result.MatchAsync(
            onSuccess: v => Task.FromResult(v * 2),
            onFailure: _ => Task.FromResult(-1));

        Assert.Equal(42, doubled);
    }

    [Fact]
    public async Task MatchAsync_Func_Failure_RunsOnFailure()
    {
        var result = Result<int>.Failure(new StringError("boom"));

        var code = await result.MatchAsync(
            onSuccess: _ => Task.FromResult("ok"),
            onFailure: e => Task.FromResult(e.Code));

        Assert.Equal("TEST", code);
    }

    [Fact]
    public async Task MatchAsync_Action_RunsCorrectBranch()
    {
        var seen = "";
        await Result<int>.Failure(new StringError("boom")).MatchAsync(
            onSuccess: _ => { seen = "success"; return Task.CompletedTask; },
            onFailure: e => { seen = e.Code; return Task.CompletedTask; });

        Assert.Equal("TEST", seen);
    }

    // =========================================================================
    // Deconstruct (2-element) (#14)
    // =========================================================================

    [Fact]
    public void Deconstruct_TwoElements_Success()
    {
        var (isSuccess, value) = Result<int>.Success(7);

        Assert.True(isSuccess);
        Assert.Equal(7, value);
    }

    [Fact]
    public void Deconstruct_TwoElements_Failure()
    {
        var (isSuccess, value) = Result<int>.Failure(new StringError("nope"));

        Assert.False(isSuccess);
        Assert.Equal(0, value);
    }
}

file sealed record ComplexTestType
{
    public required string Name { get; init; }
    public required int Value { get; init; }
}