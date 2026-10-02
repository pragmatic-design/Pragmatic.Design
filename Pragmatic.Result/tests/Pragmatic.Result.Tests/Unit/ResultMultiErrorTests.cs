// =============================================================================
// Result<TValue, TError1, TError2, ...> Multi-Error Variant Tests
// =============================================================================

using Xunit;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     Tests for source-generated Result variants with multiple error types.
/// </summary>
public class ResultMultiErrorTests
{
    // =========================================================================
    // Result<T, E1, E2> - Two Error Types
    // =========================================================================

    [Fact]
    public void Result3_Success()
    {
        var result = Result<int, TestValidationError, TestNotFoundError>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Result3_FirstError()
    {
        var result = Result<int, TestValidationError, TestNotFoundError>.Failure(
            new TestValidationError("Invalid input"));

        Assert.True(result.IsFailure);
        Assert.True(result.TryGetError1(out var error));
        Assert.Equal("Invalid input", error!.Message);
    }

    [Fact]
    public void Result3_SecondError()
    {
        var result = Result<int, TestValidationError, TestNotFoundError>.Failure(
            new TestNotFoundError("User"));

        Assert.True(result.IsFailure);
        Assert.True(result.TryGetError2(out var error));
        Assert.Equal("User", error!.Resource);
    }

    [Fact]
    public void Result3_Match()
    {
        var result = Result<int, TestValidationError, TestNotFoundError>.Failure(
            new TestValidationError("test"));

        var output = result.Match(
            v => "success",
            e1 => $"validation: {e1.Message}",
            e2 => $"notfound: {e2.Resource}");

        Assert.Equal("validation: test", output);
    }

    [Fact]
    public void Result3_ImplicitFromValue()
    {
        Result<int, TestValidationError, TestNotFoundError> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Result3_ImplicitFromError1()
    {
        Result<int, TestValidationError, TestNotFoundError> result = new TestValidationError("test");

        Assert.True(result.IsFailure);
        Assert.True(result.TryGetError1(out _));
    }

    [Fact]
    public void Result3_ImplicitFromError2()
    {
        Result<int, TestValidationError, TestNotFoundError> result = new TestNotFoundError("test");

        Assert.True(result.IsFailure);
        Assert.True(result.TryGetError2(out _));
    }

    [Fact]
    public void Result3_Map()
    {
        var result = Result<int, TestValidationError, TestNotFoundError>.Success(42);

        var mapped = result.Map(v => v.ToString());

        Assert.True(mapped.IsSuccess);
        Assert.Equal("42", mapped.Value);
    }

    [Fact]
    public void Result3_Map_PreservesError1()
    {
        var result = Result<int, TestValidationError, TestNotFoundError>.Failure(
            new TestValidationError("test"));

        var mapped = result.Map(v => v.ToString());

        Assert.True(mapped.IsFailure);
        Assert.True(mapped.TryGetError1(out var error));
        Assert.Equal("test", error!.Message);
    }

    [Fact]
    public void Result3_Map_PreservesError2()
    {
        var result = Result<int, TestValidationError, TestNotFoundError>.Failure(
            new TestNotFoundError("User"));

        var mapped = result.Map(v => v.ToString());

        Assert.True(mapped.IsFailure);
        Assert.True(mapped.TryGetError2(out var error));
        Assert.Equal("User", error!.Resource);
    }

    // =========================================================================
    // Result<T, E1, E2, E3> - Three Error Types
    // =========================================================================

    [Fact]
    public void Result4_AllErrorTypes()
    {
        var r1 = Result<int, TestValidationError, TestNotFoundError, TestUnauthorizedError>.Success(1);
        var r2 = Result<int, TestValidationError, TestNotFoundError, TestUnauthorizedError>.Failure(
            new TestValidationError("v"));
        var r3 = Result<int, TestValidationError, TestNotFoundError, TestUnauthorizedError>.Failure(
            new TestNotFoundError("n"));
        var r4 = Result<int, TestValidationError, TestNotFoundError, TestUnauthorizedError>.Failure(
            new TestUnauthorizedError());

        Assert.True(r1.IsSuccess);
        Assert.True(r2.TryGetError1(out _));
        Assert.True(r3.TryGetError2(out _));
        Assert.True(r4.TryGetError3(out _));
    }

    [Fact]
    public void Result4_Match()
    {
        var result = Result<int, TestValidationError, TestNotFoundError, TestUnauthorizedError>.Failure(
            new TestUnauthorizedError());

        var output = result.Match(
            v => "success",
            e1 => "validation",
            e2 => "notfound",
            e3 => "unauthorized");

        Assert.Equal("unauthorized", output);
    }
}