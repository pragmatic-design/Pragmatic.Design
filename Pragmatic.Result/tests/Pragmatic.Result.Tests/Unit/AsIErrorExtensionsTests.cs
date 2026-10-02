// =============================================================================
// AsIError Widening Extensions Unit Tests (#35)
// =============================================================================

using Pragmatic.Result.Extensions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class AsIErrorExtensionsTests
{
    // =========================================================================
    // Result<TValue, TError>.AsIError()
    // =========================================================================

    [Fact]
    public void AsIError_Result_Success_PreservesValue()
    {
        Result<int, TestNotFoundError> concrete = Result<int, TestNotFoundError>.Success(42);

        Result<int, IError> widened = concrete.AsIError();

        Assert.True(widened.IsSuccess);
        Assert.Equal(42, widened.Value);
    }

    [Fact]
    public void AsIError_Result_Failure_PreservesError()
    {
        var error = new TestNotFoundError("User");
        Result<int, TestNotFoundError> concrete = Result<int, TestNotFoundError>.Failure(error);

        Result<int, IError> widened = concrete.AsIError();

        Assert.True(widened.IsFailure);
        Assert.Same(error, widened.Error);
        Assert.Equal(404, widened.Error.StatusCode);
    }

    // =========================================================================
    // VoidResult<TError>.AsIError()
    // =========================================================================

    [Fact]
    public void AsIError_VoidResult_Success_StaysSuccess()
    {
        VoidResult<TestNotFoundError> concrete = VoidResult<TestNotFoundError>.Success();

        VoidResult<IError> widened = concrete.AsIError();

        Assert.True(widened.IsSuccess);
    }

    [Fact]
    public void AsIError_VoidResult_Failure_PreservesError()
    {
        var error = new TestNotFoundError("Order");
        VoidResult<TestNotFoundError> concrete = VoidResult<TestNotFoundError>.Failure(error);

        VoidResult<IError> widened = concrete.AsIError();

        Assert.True(widened.IsFailure);
        Assert.Same(error, widened.Error);
    }
}
