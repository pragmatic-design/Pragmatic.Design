// =============================================================================
// VoidResult<TError> Unit Tests
// =============================================================================

using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class VoidResultTests
{
    // =========================================================================
    // Factory Methods
    // =========================================================================

    [Fact]
    public void Success_CreatesSuccessResult()
    {
        var result = VoidResult<StringError>.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void Failure_CreatesFailureResult()
    {
        var result = VoidResult<StringError>.Failure(new StringError("error"));

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal("error", result.Error.Message);
    }

    [Fact]
    public void Failure_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            VoidResult<StringError>.Failure(null!));
    }

    // =========================================================================
    // Property Access
    // =========================================================================

    [Fact]
    public void Error_ThrowsWhenSuccess()
    {
        var result = VoidResult<StringError>.Success();

        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    // =========================================================================
    // TryGetError
    // =========================================================================

    [Fact]
    public void TryGetError_ReturnsTrueWhenFailure()
    {
        var result = VoidResult<StringError>.Failure(new StringError("error"));

        var success = result.TryGetError(out var error);

        Assert.True(success);
        Assert.Equal("error", error?.Message);
    }

    [Fact]
    public void TryGetError_ReturnsFalseWhenSuccess()
    {
        var result = VoidResult<StringError>.Success();

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
        var result = VoidResult<StringError>.Success();

        var output = result.Match(
            () => "success",
            e => $"error: {e.Message}");

        Assert.Equal("success", output);
    }

    [Fact]
    public void Match_CallsOnErrorWhenFailure()
    {
        var result = VoidResult<StringError>.Failure(new StringError("error"));

        var output = result.Match(
            () => "success",
            e => $"error: {e.Message}");

        Assert.Equal("error: error", output);
    }

    [Fact]
    public void MatchAction_CallsOnSuccessWhenSuccess()
    {
        var result = VoidResult<StringError>.Success();
        var called = false;

        result.Match(
            () => called = true,
            e => called = false);

        Assert.True(called);
    }

    [Fact]
    public void MatchAction_CallsOnErrorWhenFailure()
    {
        var result = VoidResult<StringError>.Failure(new StringError("error"));
        var called = false;

        result.Match(
            () => called = false,
            e => called = true);

        Assert.True(called);
    }

    // =========================================================================
    // Implicit Operators
    // =========================================================================

    [Fact]
    public void ImplicitOperator_FromError()
    {
        VoidResult<StringError> result = new StringError("error");

        Assert.True(result.IsFailure);
        Assert.Equal("error", result.Error.Message);
    }

    // =========================================================================
    // Implicit ToBool
    // =========================================================================

    [Fact]
    public void ImplicitToBool_TrueWhenSuccess()
    {
        var result = VoidResult<StringError>.Success();

        bool value = result;

        Assert.True(value);
    }

    [Fact]
    public void ImplicitToBool_FalseWhenFailure()
    {
        var result = VoidResult<StringError>.Failure(new StringError("error"));

        bool value = result;

        Assert.False(value);
    }

    [Fact]
    public void ImplicitToBool_InIfStatement()
    {
        var success = VoidResult<StringError>.Success();
        var failure = VoidResult<StringError>.Failure(new StringError("error"));

        var successResult = success ? "yes" : "no";
        var failureResult = failure ? "yes" : "no";

        Assert.Equal("yes", successResult);
        Assert.Equal("no", failureResult);
    }

    // =========================================================================
    // Tap Tests
    // =========================================================================

    [Fact]
    public void Tap_ExecutesActionOnSuccess()
    {
        var executed = false;
        var result = VoidResult<StringError>.Success();

        var output = result.Tap(() => executed = true);

        Assert.True(executed);
        Assert.True(output.IsSuccess);
    }

    [Fact]
    public void Tap_DoesNotExecuteOnFailure()
    {
        var executed = false;
        var result = VoidResult<StringError>.Failure(new StringError("error"));

        var output = result.Tap(() => executed = true);

        Assert.False(executed);
        Assert.True(output.IsFailure);
    }

    [Fact]
    public void Tap_ThrowsOnNullAction()
    {
        var result = VoidResult<StringError>.Success();

        Assert.Throws<ArgumentNullException>(() => result.Tap(null!));
    }

    // =========================================================================
    // OnSuccess Tests
    // =========================================================================

    [Fact]
    public void OnSuccess_ExecutesActionOnSuccess()
    {
        var executed = false;
        var result = VoidResult<StringError>.Success();

        result.OnSuccess(() => executed = true);

        Assert.True(executed);
    }

    [Fact]
    public void OnSuccess_DoesNotExecuteOnFailure()
    {
        var executed = false;
        var result = VoidResult<StringError>.Failure(new StringError("error"));

        result.OnSuccess(() => executed = true);

        Assert.False(executed);
    }

    // =========================================================================
    // OnFailure Tests
    // =========================================================================

    [Fact]
    public void OnFailure_ExecutesActionOnFailure()
    {
        var executed = false;
        var result = VoidResult<StringError>.Failure(new StringError("error"));

        var output = result.OnFailure(e => executed = true);

        Assert.True(executed);
        Assert.True(output.IsFailure);
    }

    [Fact]
    public void OnFailure_DoesNotExecuteOnSuccess()
    {
        var executed = false;
        var result = VoidResult<StringError>.Success();

        result.OnFailure(e => executed = true);

        Assert.False(executed);
    }

    [Fact]
    public void OnFailure_PassesErrorToAction()
    {
        StringError? capturedError = null;
        var error = new StringError("captured");
        var result = VoidResult<StringError>.Failure(error);

        result.OnFailure(e => capturedError = e);

        Assert.Same(error, capturedError);
    }

    [Fact]
    public void OnFailure_ThrowsOnNullAction()
    {
        var result = VoidResult<StringError>.Success();

        Assert.Throws<ArgumentNullException>(() => result.OnFailure(null!));
    }

    // =========================================================================
    // OrElse Tests
    // =========================================================================

    [Fact]
    public void OrElse_WhenSuccess_ReturnsSameResult()
    {
        var result = VoidResult<StringError>.Success();

        var output = result.OrElse(e => VoidResult<StringError>.Success());

        Assert.True(output.IsSuccess);
    }

    [Fact]
    public void OrElse_WhenFailure_ReturnsFallback()
    {
        var result = VoidResult<StringError>.Failure(new StringError("error"));

        var output = result.OrElse(e => VoidResult<StringError>.Success());

        Assert.True(output.IsSuccess);
    }

    [Fact]
    public void OrElse_PassesErrorToFallback()
    {
        var originalError = new StringError("original");
        var result = VoidResult<StringError>.Failure(originalError);

        StringError? capturedError = null;
        result.OrElse(e =>
        {
            capturedError = e;
            return VoidResult<StringError>.Success();
        });

        Assert.Same(originalError, capturedError);
    }

    [Fact]
    public void OrElse_FallbackCanReturnFailure()
    {
        var result = VoidResult<StringError>.Failure(new StringError("first"));

        var output = result.OrElse(e => VoidResult<StringError>.Failure(new StringError("fallback")));

        Assert.True(output.IsFailure);
        Assert.Equal("fallback", output.Error.Message);
    }

    [Fact]
    public void OrElse_ThrowsOnNullFallback()
    {
        var result = VoidResult<StringError>.Success();

        Assert.Throws<ArgumentNullException>(() => result.OrElse(null!));
    }

    // =========================================================================
    // MapError Tests
    // =========================================================================

    [Fact]
    public void MapError_WhenSuccess_ReturnsSameSuccess()
    {
        var result = VoidResult<StringError>.Success();

        var output = result.MapError(e => new TestValidationError(e.Message));

        Assert.True(output.IsSuccess);
    }

    [Fact]
    public void MapError_WhenFailure_MapsError()
    {
        var result = VoidResult<StringError>.Failure(new StringError("original"));

        var output = result.MapError(e => new TestValidationError($"mapped: {e.Message}"));

        Assert.True(output.IsFailure);
        Assert.IsType<TestValidationError>(output.Error);
        Assert.Equal("mapped: original", output.Error.Message);
    }

    [Fact]
    public void MapError_ThrowsOnNullMapper()
    {
        var result = VoidResult<StringError>.Success();

        Assert.Throws<ArgumentNullException>(() =>
            result.MapError((Func<StringError, TestValidationError>)null!));
    }

    // =========================================================================
    // Then Tests
    // =========================================================================

    [Fact]
    public void Then_WhenSuccess_ExecutesNext()
    {
        var result = VoidResult<StringError>.Success();
        var executed = false;

        var output = result.Then(() =>
        {
            executed = true;
            return VoidResult<StringError>.Success();
        });

        Assert.True(executed);
        Assert.True(output.IsSuccess);
    }

    [Fact]
    public void Then_WhenFailure_DoesNotExecuteNext()
    {
        var error = new StringError("original");
        var result = VoidResult<StringError>.Failure(error);
        var executed = false;

        var output = result.Then(() =>
        {
            executed = true;
            return VoidResult<StringError>.Success();
        });

        Assert.False(executed);
        Assert.True(output.IsFailure);
        Assert.Same(error, output.Error);
    }

    [Fact]
    public void Then_PropagatesFailureFromNext()
    {
        var result = VoidResult<StringError>.Success();

        var output = result.Then(() => VoidResult<StringError>.Failure(new StringError("next error")));

        Assert.True(output.IsFailure);
        Assert.Equal("next error", output.Error.Message);
    }

    [Fact]
    public void Then_ThrowsOnNullNext()
    {
        var result = VoidResult<StringError>.Success();

        Assert.Throws<ArgumentNullException>(() => result.Then(null!));
    }

    [Fact]
    public void Then_CanBeChained()
    {
        var log = new List<string>();

        var result = VoidResult<StringError>.Success()
            .Then(() =>
            {
                log.Add("1");
                return VoidResult<StringError>.Success();
            })
            .Then(() =>
            {
                log.Add("2");
                return VoidResult<StringError>.Success();
            })
            .Then(() =>
            {
                log.Add("3");
                return VoidResult<StringError>.Success();
            });

        Assert.True(result.IsSuccess);
        Assert.Equal(["1", "2", "3"], log);
    }

    [Fact]
    public void Then_StopsOnFirstFailure()
    {
        var log = new List<string>();

        var result = VoidResult<StringError>.Success()
            .Then(() =>
            {
                log.Add("1");
                return VoidResult<StringError>.Success();
            })
            .Then(() =>
            {
                log.Add("2");
                return VoidResult<StringError>.Failure(new StringError("stop"));
            })
            .Then(() =>
            {
                log.Add("3");
                return VoidResult<StringError>.Success();
            });

        Assert.True(result.IsFailure);
        Assert.Equal(["1", "2"], log);
    }

    // =========================================================================
    // IResultBase Implementation Tests
    // =========================================================================

    [Fact]
    public void IResultBase_ValueAsObject_AlwaysReturnsNull()
    {
        var success = VoidResult<StringError>.Success();
        var failure = VoidResult<StringError>.Failure(new StringError("error"));

        Assert.Null(((IResultBase)success).ValueAsObject);
        Assert.Null(((IResultBase)failure).ValueAsObject);
    }

    [Fact]
    public void IResultBase_ErrorAsObject_ReturnsNullWhenSuccess()
    {
        var result = VoidResult<StringError>.Success();

        Assert.Null(((IResultBase)result).ErrorAsObject);
    }

    [Fact]
    public void IResultBase_ErrorAsObject_ReturnsErrorWhenFailure()
    {
        var error = new StringError("error");
        var result = VoidResult<StringError>.Failure(error);

        Assert.Same(error, ((IResultBase)result).ErrorAsObject);
    }

    // =========================================================================
    // Edge Cases
    // =========================================================================

    [Fact]
    public void VoidResult_IsValueType()
    {
        Assert.True(typeof(VoidResult<StringError>).IsValueType);
    }

    [Fact]
    public void VoidResult_DefaultIsFailure()
    {
        // Default struct has IsSuccess = false (default bool)
        var result = default(VoidResult<StringError>);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void VoidResult_CanBeUsedInCollections()
    {
        var results = new List<VoidResult<StringError>>
        {
            VoidResult<StringError>.Success(),
            VoidResult<StringError>.Failure(new StringError("e1")),
            VoidResult<StringError>.Success()
        };

        Assert.Equal(2, results.Count(r => r.IsSuccess));
        Assert.Single(results, r => r.IsFailure);
    }

    [Fact]
    public void FluentChaining_ComplexPipeline()
    {
        var log = new List<string>();

        var result = VoidResult<StringError>.Success()
            .Tap(() => log.Add("start"))
            .Then(() => VoidResult<StringError>.Success())
            .Tap(() => log.Add("after then"))
            .OnSuccess(() => log.Add("success"));

        Assert.True(result.IsSuccess);
        Assert.Equal(["start", "after then", "success"], log);
    }

    [Fact]
    public void FluentChaining_WithRecovery()
    {
        var log = new List<string>();

        var result = VoidResult<StringError>.Failure(new StringError("error"))
            .OnFailure(e => log.Add($"logged: {e.Message}"))
            .OrElse(e =>
            {
                log.Add("recovered");
                return VoidResult<StringError>.Success();
            })
            .Tap(() => log.Add("continued"));

        Assert.True(result.IsSuccess);
        Assert.Equal(["logged: error", "recovered", "continued"], log);
    }
}