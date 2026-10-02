// =============================================================================
// ResultExtensions (Side-Effects, Recovery, Extraction) Unit Tests
// =============================================================================

using Pragmatic.Result.Extensions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class ResultExtensionsTests
{
    // =========================================================================
    // Tap Tests
    // =========================================================================

    [Fact]
    public void Tap_ExecutesActionOnSuccess()
    {
        var executed = false;
        var result = Result<int, StringError>.Success(42);

        var output = result.Tap(v => executed = true);

        Assert.True(executed);
        Assert.True(output.IsSuccess);
    }

    [Fact]
    public void Tap_DoesNotExecuteOnFailure()
    {
        var executed = false;
        var result = Result<int, StringError>.Failure(new StringError("error"));

        var output = result.Tap(v => executed = true);

        Assert.False(executed);
        Assert.True(output.IsFailure);
    }

    [Fact]
    public void Tap_PassesValueToAction()
    {
        var capturedValue = 0;
        var result = Result<int, StringError>.Success(42);

        result.Tap(v => capturedValue = v);

        Assert.Equal(42, capturedValue);
    }

    [Fact]
    public void Tap_ThrowsOnNullAction()
    {
        var result = Result<int, StringError>.Success(42);

        Assert.Throws<ArgumentNullException>(() => result.Tap(null!));
    }

    [Fact]
    public void Tap_CanBeChained()
    {
        var log = new List<string>();
        var result = Result<int, StringError>.Success(42);

        var output = result
            .Tap(v => log.Add($"First: {v}"))
            .Tap(v => log.Add($"Second: {v}"))
            .Tap(v => log.Add($"Third: {v}"));

        Assert.Equal(["First: 42", "Second: 42", "Third: 42"], log);
    }

    // =========================================================================
    // OnSuccess Tests (Alias for Tap)
    // =========================================================================

    [Fact]
    public void OnSuccess_ExecutesActionOnSuccess()
    {
        var executed = false;
        var result = Result<int, StringError>.Success(42);

        result.OnSuccess(v => executed = true);

        Assert.True(executed);
    }

    [Fact]
    public void OnSuccess_DoesNotExecuteOnFailure()
    {
        var executed = false;
        var result = Result<int, StringError>.Failure(new StringError("error"));

        result.OnSuccess(v => executed = true);

        Assert.False(executed);
    }

    // =========================================================================
    // OnFailure Tests
    // =========================================================================

    [Fact]
    public void OnFailure_ExecutesActionOnFailure()
    {
        var executed = false;
        var result = Result<int, StringError>.Failure(new StringError("error"));

        var output = result.OnFailure(e => executed = true);

        Assert.True(executed);
        Assert.True(output.IsFailure);
    }

    [Fact]
    public void OnFailure_DoesNotExecuteOnSuccess()
    {
        var executed = false;
        var result = Result<int, StringError>.Success(42);

        result.OnFailure(e => executed = true);

        Assert.False(executed);
    }

    [Fact]
    public void OnFailure_PassesErrorToAction()
    {
        StringError? capturedError = null;
        var error = new StringError("captured");
        var result = Result<int, StringError>.Failure(error);

        result.OnFailure(e => capturedError = e);

        Assert.Same(error, capturedError);
    }

    [Fact]
    public void OnFailure_ThrowsOnNullAction()
    {
        var result = Result<int, StringError>.Success(42);

        Assert.Throws<ArgumentNullException>(() => result.OnFailure(null!));
    }

    // =========================================================================
    // Ensure Tests
    // =========================================================================

    [Fact]
    public void Ensure_WhenPredicateTrue_ReturnsSameResult()
    {
        var result = Result<int, StringError>.Success(42);

        var output = result.Ensure(
            v => v > 0,
            v => new StringError("must be positive"));

        Assert.True(output.IsSuccess);
        Assert.Equal(42, output.Value);
    }

    [Fact]
    public void Ensure_WhenPredicateFalse_ReturnsFailure()
    {
        var result = Result<int, StringError>.Success(-5);

        var output = result.Ensure(
            v => v > 0,
            v => new StringError($"must be positive, got {v}"));

        Assert.True(output.IsFailure);
        Assert.Equal("must be positive, got -5", output.Error.Message);
    }

    [Fact]
    public void Ensure_WhenAlreadyFailure_PreservesError()
    {
        var originalError = new StringError("original");
        var result = Result<int, StringError>.Failure(originalError);

        var output = result.Ensure(
            v => v > 0,
            v => new StringError("should not be called"));

        Assert.True(output.IsFailure);
        Assert.Same(originalError, output.Error);
    }

    [Fact]
    public void Ensure_ThrowsOnNullPredicate()
    {
        var result = Result<int, StringError>.Success(42);

        Assert.Throws<ArgumentNullException>(() =>
            result.Ensure(null!, v => new StringError("error")));
    }

    [Fact]
    public void Ensure_ThrowsOnNullErrorFactory()
    {
        var result = Result<int, StringError>.Success(42);

        Assert.Throws<ArgumentNullException>(() =>
            result.Ensure(v => true, null!));
    }

    [Fact]
    public void Ensure_CanBeChained()
    {
        var result = Result<int, StringError>.Success(50)
            .Ensure(v => v > 0, v => new StringError("must be positive"))
            .Ensure(v => v < 100, v => new StringError("must be < 100"))
            .Ensure(v => v % 2 == 0, v => new StringError("must be even"));

        Assert.True(result.IsSuccess);
        Assert.Equal(50, result.Value);
    }

    [Fact]
    public void Ensure_ChainedFailsOnFirstFailure()
    {
        var result = Result<int, StringError>.Success(101)
            .Ensure(v => v > 0, v => new StringError("positive"))
            .Ensure(v => v < 100, v => new StringError("< 100"))
            .Ensure(v => v % 2 == 0, v => new StringError("even"));

        Assert.True(result.IsFailure);
        Assert.Equal("< 100", result.Error.Message);
    }

    // =========================================================================
    // OrElse Tests
    // =========================================================================

    [Fact]
    public void OrElse_WhenSuccess_ReturnsSameResult()
    {
        var result = Result<int, StringError>.Success(42);

        var output = result.OrElse(e => Result<int, StringError>.Success(0));

        Assert.True(output.IsSuccess);
        Assert.Equal(42, output.Value);
    }

    [Fact]
    public void OrElse_WhenFailure_ReturnsFallbackResult()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));

        var output = result.OrElse(e => Result<int, StringError>.Success(100));

        Assert.True(output.IsSuccess);
        Assert.Equal(100, output.Value);
    }

    [Fact]
    public void OrElse_PassesErrorToFallback()
    {
        var originalError = new StringError("original");
        var result = Result<int, StringError>.Failure(originalError);

        StringError? capturedError = null;
        result.OrElse(e =>
        {
            capturedError = e;
            return Result<int, StringError>.Success(0);
        });

        Assert.Same(originalError, capturedError);
    }

    [Fact]
    public void OrElse_FallbackCanReturnFailure()
    {
        var result = Result<int, StringError>.Failure(new StringError("first"));

        var output = result.OrElse(e => Result<int, StringError>.Failure(new StringError("fallback error")));

        Assert.True(output.IsFailure);
        Assert.Equal("fallback error", output.Error.Message);
    }

    [Fact]
    public void OrElse_ThrowsOnNullFallback()
    {
        var result = Result<int, StringError>.Success(42);

        Assert.Throws<ArgumentNullException>(() => result.OrElse(null!));
    }

    // =========================================================================
    // Recover Tests
    // =========================================================================

    [Fact]
    public void Recover_WhenSuccess_ReturnsSameResult()
    {
        var result = Result<int, StringError>.Success(42);

        var output = result.Recover(e => 0);

        Assert.True(output.IsSuccess);
        Assert.Equal(42, output.Value);
    }

    [Fact]
    public void Recover_WhenFailure_ReturnsFallbackValue()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));

        var output = result.Recover(e => 100);

        Assert.True(output.IsSuccess);
        Assert.Equal(100, output.Value);
    }

    [Fact]
    public void Recover_PassesErrorToFallback()
    {
        var originalError = new StringError("original");
        var result = Result<int, StringError>.Failure(originalError);

        StringError? capturedError = null;
        result.Recover(e =>
        {
            capturedError = e;
            return 0;
        });

        Assert.Same(originalError, capturedError);
    }

    [Fact]
    public void Recover_ThrowsOnNullFallback()
    {
        var result = Result<int, StringError>.Success(42);

        Assert.Throws<ArgumentNullException>(() => result.Recover(null!));
    }

    [Fact]
    public void Recover_CanUseDifferentValueBasedOnError()
    {
        var notFound = Result<string, Error>.Failure(new TestNotFoundError("item"));
        var validation = Result<string, Error>.Failure(new TestValidationError("invalid"));

        var recoveredNotFound = notFound.Recover(e => e is TestNotFoundError
            ? "default"
            : throw new InvalidOperationException());

        var recoveredValidation = validation.Recover(e => e is TestValidationError
            ? "corrected"
            : throw new InvalidOperationException());

        Assert.Equal("default", recoveredNotFound.Value);
        Assert.Equal("corrected", recoveredValidation.Value);
    }

    // =========================================================================
    // GetValueOrDefault Tests
    // =========================================================================

    [Fact]
    public void GetValueOrDefault_WhenSuccess_ReturnsValue()
    {
        var result = Result<int, StringError>.Success(42);

        var value = result.GetValueOrDefault(0);

        Assert.Equal(42, value);
    }

    [Fact]
    public void GetValueOrDefault_WhenFailure_ReturnsDefault()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));

        var value = result.GetValueOrDefault(100);

        Assert.Equal(100, value);
    }

    [Fact]
    public void GetValueOrDefault_WithFactory_WhenSuccess_DoesNotInvokeFactory()
    {
        var result = Result<int, StringError>.Success(42);
        var factoryCalled = false;

        var value = result.GetValueOrDefault(() =>
        {
            factoryCalled = true;
            return 100;
        });

        Assert.Equal(42, value);
        Assert.False(factoryCalled);
    }

    [Fact]
    public void GetValueOrDefault_WithFactory_WhenFailure_InvokesFactory()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));
        var factoryCalled = false;

        var value = result.GetValueOrDefault(() =>
        {
            factoryCalled = true;
            return 100;
        });

        Assert.Equal(100, value);
        Assert.True(factoryCalled);
    }

    [Fact]
    public void GetValueOrDefault_WithFactory_ThrowsOnNull()
    {
        var result = Result<int, StringError>.Success(42);

        Assert.Throws<ArgumentNullException>(() =>
            result.GetValueOrDefault(null!));
    }

    // =========================================================================
    // GetValueOrThrow Tests
    // =========================================================================

    [Fact]
    public void GetValueOrThrow_WhenSuccess_ReturnsValue()
    {
        var result = Result<int, StringError>.Success(42);

        var value = result.GetValueOrThrow();

        Assert.Equal(42, value);
    }

    [Fact]
    public void GetValueOrThrow_WhenFailure_ThrowsInvalidOperationException()
    {
        var result = Result<int, StringError>.Failure(new StringError("test error"));

        var ex = Assert.Throws<InvalidOperationException>(() => result.GetValueOrThrow());
        Assert.Contains("TEST", ex.Message); // Contains error code
    }

    [Fact]
    public void GetValueOrThrow_ExceptionMessageContainsErrorCode()
    {
        var result = Result<int, TestNotFoundError>.Failure(new TestNotFoundError("item"));

        var ex = Assert.Throws<InvalidOperationException>(() => result.GetValueOrThrow());

        Assert.Contains("NOT_FOUND", ex.Message);
    }

    // =========================================================================
    // Combined Usage Patterns
    // =========================================================================

    [Fact]
    public void FluentChaining_ComplexPipeline()
    {
        var log = new List<string>();

        var result = Result<int, StringError>.Success(10)
            .Tap(v => log.Add($"Initial: {v}"))
            .Ensure(v => v > 0, v => new StringError("positive"))
            .Map(v => v * 2)
            .Tap(v => log.Add($"After double: {v}"))
            .Ensure(v => v < 100, v => new StringError("< 100"));

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Value);
        Assert.Equal(["Initial: 10", "After double: 20"], log);
    }

    [Fact]
    public void FluentChaining_WithRecovery()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"))
            .OnFailure(e => Console.WriteLine($"Logged: {e.Message}"))
            .Recover(e => 0)
            .Map(v => v + 10);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public void FluentChaining_OnSuccessAndOnFailure()
    {
        var successLog = new List<string>();
        var failureLog = new List<string>();

        var success = Result<int, StringError>.Success(42)
            .OnSuccess(v => successLog.Add($"Success: {v}"))
            .OnFailure(e => failureLog.Add($"Failure: {e.Message}"));

        var failure = Result<int, StringError>.Failure(new StringError("error"))
            .OnSuccess(v => successLog.Add($"Success: {v}"))
            .OnFailure(e => failureLog.Add($"Failure: {e.Message}"));

        Assert.Single(successLog);
        Assert.Single(failureLog);
        Assert.Equal("Success: 42", successLog[0]);
        Assert.Equal("Failure: error", failureLog[0]);
    }
}