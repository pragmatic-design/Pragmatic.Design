// =============================================================================
// Combine Extensions Unit Tests
// =============================================================================

using Pragmatic.Result.Extensions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class CombineExtensionsTests
{
    // =========================================================================
    // Combine<T1, T2> Tests
    // =========================================================================

    [Fact]
    public void Combine_TwoSuccesses_ReturnsTupleSuccess()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<string, StringError>.Success("two");

        var combined = ResultExtensions.Combine(r1, r2);

        Assert.True(combined.IsSuccess);
        Assert.Equal((1, "two"), combined.Value);
    }

    [Fact]
    public void Combine_FirstFailure_ReturnsFirstError()
    {
        var r1 = Result<int, StringError>.Failure(new StringError("first"));
        var r2 = Result<string, StringError>.Success("two");

        var combined = ResultExtensions.Combine(r1, r2);

        Assert.True(combined.IsFailure);
        Assert.Equal("first", combined.Error.Message);
    }

    [Fact]
    public void Combine_SecondFailure_ReturnsSecondError()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<string, StringError>.Failure(new StringError("second"));

        var combined = ResultExtensions.Combine(r1, r2);

        Assert.True(combined.IsFailure);
        Assert.Equal("second", combined.Error.Message);
    }

    [Fact]
    public void Combine_BothFailure_ReturnsFirstError_FailFast()
    {
        var r1 = Result<int, StringError>.Failure(new StringError("first"));
        var r2 = Result<string, StringError>.Failure(new StringError("second"));

        var combined = ResultExtensions.Combine(r1, r2);

        Assert.True(combined.IsFailure);
        Assert.Equal("first", combined.Error.Message); // Fail-fast: returns first error
    }

    // =========================================================================
    // Combine<T1, T2, T3> Tests
    // =========================================================================

    [Fact]
    public void Combine_ThreeSuccesses_ReturnsTupleSuccess()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<string, StringError>.Success("two");
        var r3 = Result<bool, StringError>.Success(true);

        var combined = ResultExtensions.Combine(r1, r2, r3);

        Assert.True(combined.IsSuccess);
        Assert.Equal((1, "two", true), combined.Value);
    }

    [Fact]
    public void Combine_ThirdFailure_ReturnsThirdError()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<string, StringError>.Success("two");
        var r3 = Result<bool, StringError>.Failure(new StringError("third"));

        var combined = ResultExtensions.Combine(r1, r2, r3);

        Assert.True(combined.IsFailure);
        Assert.Equal("third", combined.Error.Message);
    }

    [Fact]
    public void Combine_MiddleFailure_ReturnsMiddleError()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<string, StringError>.Failure(new StringError("middle"));
        var r3 = Result<bool, StringError>.Failure(new StringError("third"));

        var combined = ResultExtensions.Combine(r1, r2, r3);

        Assert.True(combined.IsFailure);
        Assert.Equal("middle", combined.Error.Message); // Fail-fast
    }

    // =========================================================================
    // Combine<T1, T2, T3, T4> Tests
    // =========================================================================

    [Fact]
    public void Combine_FourSuccesses_ReturnsTupleSuccess()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<string, StringError>.Success("two");
        var r3 = Result<bool, StringError>.Success(true);
        var r4 = Result<double, StringError>.Success(4.0);

        var combined = ResultExtensions.Combine(r1, r2, r3, r4);

        Assert.True(combined.IsSuccess);
        Assert.Equal((1, "two", true, 4.0), combined.Value);
    }

    [Fact]
    public void Combine_FourthFailure_ReturnsFourthError()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<string, StringError>.Success("two");
        var r3 = Result<bool, StringError>.Success(true);
        var r4 = Result<double, StringError>.Failure(new StringError("fourth"));

        var combined = ResultExtensions.Combine(r1, r2, r3, r4);

        Assert.True(combined.IsFailure);
        Assert.Equal("fourth", combined.Error.Message);
    }

    // =========================================================================
    // Combine<T1, T2, T3, T4, T5> Tests
    // =========================================================================

    [Fact]
    public void Combine_FiveSuccesses_ReturnsTupleSuccess()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<string, StringError>.Success("two");
        var r3 = Result<bool, StringError>.Success(true);
        var r4 = Result<double, StringError>.Success(4.0);
        var r5 = Result<char, StringError>.Success('5');

        var combined = ResultExtensions.Combine(r1, r2, r3, r4, r5);

        Assert.True(combined.IsSuccess);
        Assert.Equal((1, "two", true, 4.0, '5'), combined.Value);
    }

    [Fact]
    public void Combine_FifthFailure_ReturnsFifthError()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<string, StringError>.Success("two");
        var r3 = Result<bool, StringError>.Success(true);
        var r4 = Result<double, StringError>.Success(4.0);
        var r5 = Result<char, StringError>.Failure(new StringError("fifth"));

        var combined = ResultExtensions.Combine(r1, r2, r3, r4, r5);

        Assert.True(combined.IsFailure);
        Assert.Equal("fifth", combined.Error.Message);
    }

    // =========================================================================
    // Edge Cases
    // =========================================================================

    [Fact]
    public void Combine_WithDifferentValueTypes_Works()
    {
        var r1 = Result<int, StringError>.Success(42);
        var r2 = Result<Guid, StringError>.Success(Guid.NewGuid());

        var combined = ResultExtensions.Combine(r1, r2);

        Assert.True(combined.IsSuccess);
        Assert.Equal(42, combined.Value.Item1);
    }

    [Fact]
    public void Combine_CanBeChained()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<string, StringError>.Success("two");

        var combined = ResultExtensions.Combine(r1, r2);

        // Can map the tuple
        var mapped = combined.Map(t => $"{t.Item1}-{t.Item2}");

        Assert.True(mapped.IsSuccess);
        Assert.Equal("1-two", mapped.Value);
    }

    [Fact]
    public void Combine_WithReferenceTypeValues_Works()
    {
        // Result<T>.Success throws on null, so we use non-null values
        var r1 = Result<string, StringError>.Success("first");
        var r2 = Result<string, StringError>.Success("second");

        var combined = ResultExtensions.Combine(r1, r2);

        Assert.True(combined.IsSuccess);
        Assert.Equal("first", combined.Value.Item1);
        Assert.Equal("second", combined.Value.Item2);
    }

    [Fact]
    public void Success_ThrowsOnNullValue()
    {
        // Result<T>.Success correctly rejects null values
        Assert.Throws<ArgumentNullException>(() =>
            Result<string?, StringError>.Success(null));
    }

    [Fact]
    public void Combine_PreservesErrorType()
    {
        var r1 = Result<int, TestValidationError>.Success(1);
        var r2 = Result<string, TestValidationError>.Failure(new TestValidationError("validation failed"));

        var combined = ResultExtensions.Combine(r1, r2);

        Assert.True(combined.IsFailure);
        Assert.IsType<TestValidationError>(combined.Error);
    }

    // =========================================================================
    // Deconstructing Combined Results
    // =========================================================================

    [Fact]
    public void Combine_ResultCanBeDeconstructed()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<string, StringError>.Success("two");

        var combined = ResultExtensions.Combine(r1, r2);

        var (isSuccess, value, error) = combined;

        Assert.True(isSuccess);
        Assert.Equal((1, "two"), value);
        Assert.Null(error);
    }

    [Fact]
    public void Combine_FailureCanBeDeconstructed()
    {
        var r1 = Result<int, StringError>.Failure(new StringError("err"));
        var r2 = Result<string, StringError>.Success("two");

        var combined = ResultExtensions.Combine(r1, r2);

        var (isSuccess, value, error) = combined;

        Assert.False(isSuccess);
        Assert.Equal(default, value);
        Assert.NotNull(error);
    }
}