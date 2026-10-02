using Pragmatic.Testing.Assertions;
using Pragmatic.Result.Http;

namespace Pragmatic.Result.AspNetCore.Tests;

/// <summary>
///     Tests for IResultBase interface implementation used by filters.
/// </summary>
public class IResultBaseTests
{
    #region Type Detection

    [Fact]
    public void CanDetectResultType_AtRuntime()
    {
        object successResult = (Result<string, NotFoundError>)"value";
        object failureResult = (Result<string, NotFoundError>)NotFoundError.Create("User");
        object voidSuccess = VoidResult<NotFoundError>.Success();
        object voidFailure = (VoidResult<NotFoundError>)NotFoundError.Create("User");
        object nonResult = "plain string";

        (successResult is IResultBase).Should().BeTrue();
        (failureResult is IResultBase).Should().BeTrue();
        (voidSuccess is IResultBase).Should().BeTrue();
        (voidFailure is IResultBase).Should().BeTrue();
        (nonResult is IResultBase).Should().BeFalse();
    }

    #endregion

    #region Result<TValue, TError> implements IResultBase

    [Fact]
    public void Result_ImplementsIResultBase()
    {
        Result<string, NotFoundError> result = "value";

        result.Should().BeAssignableTo<IResultBase>();
    }

    [Fact]
    public void Result_Success_IsSuccessReturnsTrue()
    {
        Result<string, NotFoundError> result = "value";
        IResultBase resultBase = result;

        resultBase.IsSuccess.Should().BeTrue();
        resultBase.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void Result_Failure_IsFailureReturnsTrue()
    {
        Result<string, NotFoundError> result = NotFoundError.Create("User");
        IResultBase resultBase = result;

        resultBase.IsSuccess.Should().BeFalse();
        resultBase.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Result_Success_ValueAsObjectReturnsValue()
    {
        Result<string, NotFoundError> result = "test value";
        IResultBase resultBase = result;

        resultBase.ValueAsObject.Should().Be("test value");
    }

    [Fact]
    public void Result_Failure_ValueAsObjectReturnsNull()
    {
        Result<string, NotFoundError> result = NotFoundError.Create("User");
        IResultBase resultBase = result;

        resultBase.ValueAsObject.Should().BeNull();
    }

    [Fact]
    public void Result_Success_ErrorAsObjectReturnsNull()
    {
        Result<string, NotFoundError> result = "value";
        IResultBase resultBase = result;

        resultBase.ErrorAsObject.Should().BeNull();
    }

    [Fact]
    public void Result_Failure_ErrorAsObjectReturnsError()
    {
        var error = NotFoundError.Create("User", "123");
        Result<string, NotFoundError> result = error;
        IResultBase resultBase = result;

        resultBase.ErrorAsObject.Should().NotBeNull();
        resultBase.ErrorAsObject!.Code.Should().Be("NOT_FOUND");
    }

    #endregion

    #region VoidResult<TError> implements IResultBase

    [Fact]
    public void VoidResult_ImplementsIResultBase()
    {
        var result = VoidResult<NotFoundError>.Success();

        result.Should().BeAssignableTo<IResultBase>();
    }

    [Fact]
    public void VoidResult_Success_IsSuccessReturnsTrue()
    {
        var result = VoidResult<NotFoundError>.Success();
        IResultBase resultBase = result;

        resultBase.IsSuccess.Should().BeTrue();
        resultBase.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void VoidResult_Failure_IsFailureReturnsTrue()
    {
        VoidResult<NotFoundError> result = NotFoundError.Create("User");
        IResultBase resultBase = result;

        resultBase.IsSuccess.Should().BeFalse();
        resultBase.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void VoidResult_Success_ValueAsObjectReturnsNull()
    {
        var result = VoidResult<NotFoundError>.Success();
        IResultBase resultBase = result;

        // VoidResult has no value, even on success
        resultBase.ValueAsObject.Should().BeNull();
    }

    [Fact]
    public void VoidResult_Success_ErrorAsObjectReturnsNull()
    {
        var result = VoidResult<NotFoundError>.Success();
        IResultBase resultBase = result;

        resultBase.ErrorAsObject.Should().BeNull();
    }

    [Fact]
    public void VoidResult_Failure_ErrorAsObjectReturnsError()
    {
        var error = NotFoundError.Create("User", "123");
        VoidResult<NotFoundError> result = error;
        IResultBase resultBase = result;

        resultBase.ErrorAsObject.Should().NotBeNull();
        resultBase.ErrorAsObject!.Code.Should().Be("NOT_FOUND");
    }

    #endregion

    #region Generated Multi-Error Result variants implement IResultBase

    [Fact]
    public void Result3_ImplementsIResultBase()
    {
        Result<string, NotFoundError, UnauthorizedError> result = "value";

        result.Should().BeAssignableTo<IResultBase>();
    }

    [Fact]
    public void Result3_Success_IResultBaseProperties()
    {
        Result<string, NotFoundError, UnauthorizedError> result = "test value";
        IResultBase resultBase = result;

        resultBase.IsSuccess.Should().BeTrue();
        resultBase.IsFailure.Should().BeFalse();
        resultBase.ValueAsObject.Should().Be("test value");
        resultBase.ErrorAsObject.Should().BeNull();
    }

    [Fact]
    public void Result3_FailureWithError1_IResultBaseProperties()
    {
        var error = NotFoundError.Create("User", "123");
        Result<string, NotFoundError, UnauthorizedError> result = error;
        IResultBase resultBase = result;

        resultBase.IsSuccess.Should().BeFalse();
        resultBase.IsFailure.Should().BeTrue();
        resultBase.ValueAsObject.Should().BeNull();
        resultBase.ErrorAsObject.Should().NotBeNull();
        resultBase.ErrorAsObject!.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public void Result3_FailureWithError2_IResultBaseProperties()
    {
        var error = UnauthorizedError.Create();
        Result<string, NotFoundError, UnauthorizedError> result = error;
        IResultBase resultBase = result;

        resultBase.IsSuccess.Should().BeFalse();
        resultBase.IsFailure.Should().BeTrue();
        resultBase.ValueAsObject.Should().BeNull();
        resultBase.ErrorAsObject.Should().NotBeNull();
        resultBase.ErrorAsObject!.Code.Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public void VoidResult2_ImplementsIResultBase()
    {
        var result = VoidResult<NotFoundError, UnauthorizedError>.Success();

        result.Should().BeAssignableTo<IResultBase>();
    }

    [Fact]
    public void VoidResult2_Success_IResultBaseProperties()
    {
        var result = VoidResult<NotFoundError, UnauthorizedError>.Success();
        IResultBase resultBase = result;

        resultBase.IsSuccess.Should().BeTrue();
        resultBase.IsFailure.Should().BeFalse();
        resultBase.ValueAsObject.Should().BeNull();
        resultBase.ErrorAsObject.Should().BeNull();
    }

    [Fact]
    public void VoidResult2_FailureWithError1_IResultBaseProperties()
    {
        var error = NotFoundError.Create("User");
        VoidResult<NotFoundError, UnauthorizedError> result = error;
        IResultBase resultBase = result;

        resultBase.IsSuccess.Should().BeFalse();
        resultBase.IsFailure.Should().BeTrue();
        resultBase.ValueAsObject.Should().BeNull();
        resultBase.ErrorAsObject.Should().NotBeNull();
        resultBase.ErrorAsObject!.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public void VoidResult2_FailureWithError2_IResultBaseProperties()
    {
        var error = UnauthorizedError.Create();
        VoidResult<NotFoundError, UnauthorizedError> result = error;
        IResultBase resultBase = result;

        resultBase.IsSuccess.Should().BeFalse();
        resultBase.IsFailure.Should().BeTrue();
        resultBase.ValueAsObject.Should().BeNull();
        resultBase.ErrorAsObject.Should().NotBeNull();
        resultBase.ErrorAsObject!.Code.Should().Be("UNAUTHORIZED");
    }

    #endregion
}