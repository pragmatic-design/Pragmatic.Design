// =============================================================================
// Result<TValue, TError> Unit Tests
// =============================================================================

using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class ResultTests
{
    // =========================================================================
    // Factory Methods
    // =========================================================================

    [Fact]
    public void Success_CreatesSuccessResult()
    {
        var result = Result<int, StringError>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Success_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Result<string, StringError>.Success(null!));
    }

    [Fact]
    public void Failure_CreatesFailureResult()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal("error", result.Error.Message);
    }

    [Fact]
    public void Failure_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Result<int, StringError>.Failure(null!));
    }

    // =========================================================================
    // Property Access
    // =========================================================================

    [Fact]
    public void Value_ThrowsWhenFailure()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));

        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Error_ThrowsWhenSuccess()
    {
        var result = Result<int, StringError>.Success(42);

        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    // =========================================================================
    // TryGet Methods
    // =========================================================================

    [Fact]
    public void TryGetValue_ReturnsTrueWhenSuccess()
    {
        var result = Result<int, StringError>.Success(42);

        var success = result.TryGetValue(out var value);

        Assert.True(success);
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryGetValue_ReturnsFalseWhenFailure()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));

        var success = result.TryGetValue(out var value);

        Assert.False(success);
        Assert.Equal(default, value);
    }

    [Fact]
    public void TryGetError_ReturnsTrueWhenFailure()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));

        var success = result.TryGetError(out var error);

        Assert.True(success);
        Assert.Equal("error", error?.Message);
    }

    [Fact]
    public void TryGetError_ReturnsFalseWhenSuccess()
    {
        var result = Result<int, StringError>.Success(42);

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
        var result = Result<int, StringError>.Success(42);

        var output = result.Match(
            v => $"value: {v}",
            e => $"error: {e.Message}");

        Assert.Equal("value: 42", output);
    }

    [Fact]
    public void Match_CallsOnErrorWhenFailure()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));

        var output = result.Match(
            v => $"value: {v}",
            e => $"error: {e.Message}");

        Assert.Equal("error: error", output);
    }

    [Fact]
    public void MatchAction_CallsOnSuccessWhenSuccess()
    {
        var result = Result<int, StringError>.Success(42);
        var called = false;

        result.Match(
            v => called = true,
            e => called = false);

        Assert.True(called);
    }

    // =========================================================================
    // Map Method
    // =========================================================================

    [Fact]
    public void Map_TransformsValueWhenSuccess()
    {
        var result = Result<int, StringError>.Success(42);

        var mapped = result.Map(v => v * 2);

        Assert.True(mapped.IsSuccess);
        Assert.Equal(84, mapped.Value);
    }

    [Fact]
    public void Map_PreservesErrorWhenFailure()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));

        var mapped = result.Map(v => v * 2);

        Assert.True(mapped.IsFailure);
        Assert.Equal("error", mapped.Error.Message);
    }

    // =========================================================================
    // Bind Method
    // =========================================================================

    [Fact]
    public void Bind_ChainsWhenSuccess()
    {
        var result = Result<int, StringError>.Success(42);

        var bound = result.Bind(v =>
            Result<string, StringError>.Success($"value: {v}"));

        Assert.True(bound.IsSuccess);
        Assert.Equal("value: 42", bound.Value);
    }

    [Fact]
    public void Bind_PreservesErrorWhenFailure()
    {
        var result = Result<int, StringError>.Failure(new StringError("original error"));

        var bound = result.Bind(v =>
            Result<string, StringError>.Success($"value: {v}"));

        Assert.True(bound.IsFailure);
        Assert.Equal("original error", bound.Error.Message);
    }

    [Fact]
    public void Bind_PropagatesNewError()
    {
        var result = Result<int, StringError>.Success(42);

        var bound = result.Bind(v =>
            Result<string, StringError>.Failure(new StringError("new error")));

        Assert.True(bound.IsFailure);
        Assert.Equal("new error", bound.Error.Message);
    }

    // =========================================================================
    // Implicit Operators
    // =========================================================================

    [Fact]
    public void ImplicitOperator_FromValue()
    {
        Result<int, StringError> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ImplicitOperator_FromError()
    {
        Result<int, StringError> result = new StringError("error");

        Assert.True(result.IsFailure);
        Assert.Equal("error", result.Error.Message);
    }

    // =========================================================================
    // Deconstruct
    // =========================================================================

    [Fact]
    public void Deconstruct_Success()
    {
        var result = Result<int, StringError>.Success(42);

        var (isSuccess, value, error) = result;

        Assert.True(isSuccess);
        Assert.Equal(42, value);
        Assert.Null(error);
    }

    [Fact]
    public void Deconstruct_Failure()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));

        var (isSuccess, value, error) = result;

        Assert.False(isSuccess);
        Assert.Equal(default, value);
        Assert.Equal("error", error?.Message);
    }
}