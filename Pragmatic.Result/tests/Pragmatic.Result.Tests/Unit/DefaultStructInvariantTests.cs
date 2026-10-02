// =============================================================================
// Default / Uninitialized Struct Invariant Tests
//
// A default(Result) / default(VoidResult) is NOT a valid value: it has
// IsSuccess == false and a null error. It must NEVER be surfaced as a genuine
// failure carrying a null error (which callers would propagate as Failure(null)),
// and accessing its Error must throw a clear exception.
// =============================================================================

using Pragmatic.Result.Extensions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class DefaultStructInvariantTests
{
    // =========================================================================
    // Result<TValue, TError>
    // =========================================================================

    [Fact]
    public void Result_Default_TryGetError_DoesNotYieldNullErrorFailure()
    {
        var result = default(Result<int, StringError>);

        // Must NOT report a failure with a null error.
        var isError = result.TryGetError(out var error);

        Assert.False(isError);
        Assert.Null(error);
    }

    [Fact]
    public void Result_Default_AccessError_ThrowsUninitialized()
    {
        var result = default(Result<int, StringError>);

        var ex = Assert.Throws<InvalidOperationException>(() => _ = result.Error);
        Assert.Contains("uninitialized", ex.Message.ToLower());
    }

    [Fact]
    public void Result_Default_StillReportsFailureState()
    {
        // Back-compat: a default struct still reports IsFailure (existing MisuseTests rely on this).
        var result = default(Result<int, StringError>);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
    }

    // =========================================================================
    // Result<TValue> (untyped error)
    // =========================================================================

    [Fact]
    public void ResultUntyped_Default_TryGetError_DoesNotYieldNullErrorFailure()
    {
        var result = default(Result<int>);

        var isError = result.TryGetError(out var error);

        Assert.False(isError);
        Assert.Null(error);
    }

    [Fact]
    public void ResultUntyped_Default_AccessError_ThrowsUninitialized()
    {
        var result = default(Result<int>);

        var ex = Assert.Throws<InvalidOperationException>(() => _ = result.Error);
        Assert.Contains("uninitialized", ex.Message.ToLower());
    }

    // =========================================================================
    // VoidResult<TError>
    // =========================================================================

    [Fact]
    public void VoidResult_Default_TryGetError_DoesNotYieldNullErrorFailure()
    {
        var result = default(VoidResult<StringError>);

        var isError = result.TryGetError(out var error);

        Assert.False(isError);
        Assert.Null(error);
    }

    [Fact]
    public void VoidResult_Default_AccessError_ThrowsUninitialized()
    {
        var result = default(VoidResult<StringError>);

        var ex = Assert.Throws<InvalidOperationException>(() => _ = result.Error);
        Assert.Contains("uninitialized", ex.Message.ToLower());
    }

    // =========================================================================
    // Combine must not propagate a default input as Failure(null)
    // =========================================================================

    [Fact]
    public void Combine_DefaultInput_DoesNotProduceNullErrorFailure()
    {
        var r1 = default(Result<int, StringError>); // uninitialized
        var r2 = Result<string, StringError>.Success("two");

        // The trap: TryGetError(out null) => true => Result.Failure(null) => ArgumentNullException
        // inside Failure, or a Result carrying a null error. A default input falls through instead
        // and surfaces loudly on Value access — never a null-error failure.
        var ex = Record.Exception(() => ResultExtensions.Combine(r1, r2));

        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void Combine_EnumerableWithDefaultInput_DoesNotProduceNullErrorFailure()
    {
        var results = new[]
        {
            Result<int, StringError>.Success(1),
            default, // uninitialized
        };

        var ex = Record.Exception(() => ResultExtensions.Combine(results));

        // Never an ArgumentNullException from Failure(null); the default surfaces via Value access.
        Assert.IsType<InvalidOperationException>(ex);
    }

    // =========================================================================
    // Back-compat: genuine Success / Failure are unaffected
    // =========================================================================

    [Fact]
    public void Result_GenuineFailure_TryGetError_ReturnsErrorUnchanged()
    {
        var error = new StringError("boom");
        var result = Result<int, StringError>.Failure(error);

        Assert.True(result.TryGetError(out var got));
        Assert.Same(error, got);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Result_GenuineSuccess_TryGetError_ReturnsFalse()
    {
        var result = Result<int, StringError>.Success(42);

        Assert.False(result.TryGetError(out var got));
        Assert.Null(got);
        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void ResultUntyped_GenuineFailure_TryGetError_ReturnsErrorUnchanged()
    {
        var error = new StringError("boom");
        var result = Result<int>.Failure(error);

        Assert.True(result.TryGetError(out var got));
        Assert.Same(error, got);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void VoidResult_GenuineFailure_TryGetError_ReturnsErrorUnchanged()
    {
        var error = new StringError("boom");
        var result = VoidResult<StringError>.Failure(error);

        Assert.True(result.TryGetError(out var got));
        Assert.Same(error, got);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void VoidResult_GenuineSuccess_TryGetError_ReturnsFalse()
    {
        var result = VoidResult<StringError>.Success();

        Assert.False(result.TryGetError(out var got));
        Assert.Null(got);
    }

    [Fact]
    public void Combine_AllGenuineSuccesses_StillWorks()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<string, StringError>.Success("two");

        var combined = ResultExtensions.Combine(r1, r2);

        Assert.True(combined.IsSuccess);
        Assert.Equal((1, "two"), combined.Value);
    }

    [Fact]
    public void Combine_GenuineFailure_StillReturnsThatError()
    {
        var r1 = Result<int, StringError>.Failure(new StringError("first"));
        var r2 = Result<string, StringError>.Success("two");

        var combined = ResultExtensions.Combine(r1, r2);

        Assert.True(combined.IsFailure);
        Assert.Equal("first", combined.Error.Message);
    }
}
