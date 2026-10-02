// =============================================================================
// Misuse Tests
// Tests that verify proper handling of common mistakes
// =============================================================================

using Xunit;

namespace Pragmatic.Result.Tests.Misuse;

/// <summary>
///     Tests that verify common misuse scenarios are properly handled.
/// </summary>
public class MisuseTests
{
    // =========================================================================
    // Accessing Value/Error without checking
    // =========================================================================

    [Fact]
    public void Result_AccessValueWithoutChecking_Throws()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));

        var ex = Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        Assert.Contains("failure", ex.Message.ToLower());
    }

    [Fact]
    public void Result_AccessErrorWithoutChecking_Throws()
    {
        var result = Result<int, StringError>.Success(42);

        var ex = Assert.Throws<InvalidOperationException>(() => _ = result.Error);
        Assert.Contains("success", ex.Message.ToLower());
    }

    [Fact]
    public void VoidResult_AccessErrorWithoutChecking_Throws()
    {
        var result = VoidResult<StringError>.Success();

        var ex = Assert.Throws<InvalidOperationException>(() => _ = result.Error);
        // VoidResult uses "succeeded" not "success" in its error message
        Assert.Contains("succeed", ex.Message.ToLower());
    }

    [Fact]
    public void Maybe_AccessValueWithoutChecking_Throws()
    {
        var maybe = Maybe<int>.None();

        var ex = Assert.Throws<InvalidOperationException>(() => _ = maybe.Value);
        Assert.Contains("none", ex.Message.ToLower());
    }

    // =========================================================================
    // Null values
    // =========================================================================

    [Fact]
    public void Result_NullValue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Result<string, StringError>.Success(null!));
    }

    [Fact]
    public void Result_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Result<int, StringError>.Failure(null!));
    }

    [Fact]
    public void VoidResult_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            VoidResult<StringError>.Failure(null!));
    }

    [Fact]
    public void Maybe_NullValue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Maybe<string>.Some(null!));
    }

    // =========================================================================
    // Multi-error TryGetError on wrong error type
    // =========================================================================

    [Fact]
    public void Result3_TryGetWrongErrorType_ReturnsFalse()
    {
        var result = Result<int, TestValidationError, TestNotFoundError>.Failure(
            new TestValidationError("error"));

        // Correct error type
        Assert.True(result.TryGetError1(out var error1));
        Assert.Equal("error", error1!.Message);

        // Wrong error type
        Assert.False(result.TryGetError2(out var error2));
        Assert.Null(error2);
    }

    [Fact]
    public void Result3_AccessWrongErrorProperty_Throws()
    {
        var result = Result<int, TestValidationError, TestNotFoundError>.Failure(
            new TestValidationError("error"));

        // Correct error type
        Assert.Equal("error", result.Error1.Message);

        // Wrong error type should throw
        Assert.Throws<InvalidOperationException>(() => _ = result.Error2);
    }

    // =========================================================================
    // Using TryGet pattern correctly
    // =========================================================================

    [Fact]
    public void Result_ProperTryGetPattern()
    {
        var result = Result<int, StringError>.Success(42);

        // This is the recommended pattern
        if (result.TryGetValue(out var value))
            Assert.Equal(42, value);
        else if (result.TryGetError(out var error))
            Assert.Fail($"Unexpected error: {error?.Message}");
    }

    [Fact]
    public void Result_ProperMatchPattern()
    {
        var result = Result<int, StringError>.Success(42);

        // Match is always safe - both branches are required
        var output = result.Match(
            v => v * 2,
            e => -1);

        Assert.Equal(84, output);
    }

    // =========================================================================
    // Default struct behavior
    // =========================================================================

    [Fact]
    public void Result_DefaultStruct_IsInvalidState()
    {
        // Using default struct is a misuse - but we handle it gracefully
        var result = default(Result<int, StringError>);

        // Default has _isSuccess = false (default bool)
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void VoidResult_DefaultStruct_IsInvalidState()
    {
        var result = default(VoidResult<StringError>);
        // Default has _isSuccess = false
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Maybe_DefaultStruct_IsNone()
    {
        var maybe = default(Maybe<int>);
        Assert.False(maybe.HasValue);
    }
}