using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Pragmatic.Result.Http;

namespace Pragmatic.Result.AspNetCore.Tests;

public class ResultHttpExtensionsTests
{
    #region Custom Error Type

    [Fact]
    public void ToHttpResult_WithCustomError_ReturnsCorrectStatusCode()
    {
        Result<string, TestError> result = new TestError("TEST_ERROR");

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(400);
    }

    #endregion

    private sealed record TestError(string Code) : Error
    {
        public override string Code { get; } = Code;
        public override int StatusCode => 400;
    }

    #region ToHttpResult - Success Cases

    [Fact]
    public void ToHttpResult_WithSuccess_ReturnsOkResult()
    {
        Result<string, NotFoundError> result = "test value";

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<Ok<string>>();
        var okResult = (Ok<string>)httpResult;
        okResult.Value.Should().Be("test value");
    }

    [Fact]
    public void ToHttpResult_WithSuccessAnd201_ReturnsCreatedResult()
    {
        Result<string, NotFoundError> result = "created item";

        var httpResult = result.ToHttpResult(StatusCodes.Status201Created);

        httpResult.Should().BeOfType<Created<string>>();
    }

    [Fact]
    public void ToHttpResult_WithSuccessAnd204_ReturnsNoContentResult()
    {
        Result<string, NotFoundError> result = "ignored";

        var httpResult = result.ToHttpResult(StatusCodes.Status204NoContent);

        httpResult.Should().BeOfType<NoContent>();
    }

    #endregion

    #region ToHttpResult - Failure Cases

    [Fact]
    public void ToHttpResult_WithNotFoundError_ReturnsProblemWith404()
    {
        Result<string, NotFoundError> result = NotFoundError.Create("User", "123");

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(404);
    }

    [Fact]
    public void ToHttpResult_WithUnauthorizedError_ReturnsProblemWith401()
    {
        Result<string, UnauthorizedError> result = new UnauthorizedError();

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public void ToHttpResult_WithForbiddenError_ReturnsProblemWith403()
    {
        Result<string, ForbiddenError> result = ForbiddenError.Create("admin", "delete");

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public void ToHttpResult_WithConflictError_ReturnsProblemWith409()
    {
        Result<string, ConflictError> result = ConflictError.AlreadyExists("User", "email");

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(409);
    }

    #endregion

    #region ToCreatedResult

    [Fact]
    public void ToCreatedResult_WithSuccess_ReturnsCreatedResult()
    {
        Result<string, NotFoundError> result = "new item";

        var httpResult = result.ToCreatedResult("/api/items/1");

        httpResult.Should().BeOfType<Created<string>>();
        var createdResult = (Created<string>)httpResult;
        createdResult.Location.Should().Be("/api/items/1");
        createdResult.Value.Should().Be("new item");
    }

    [Fact]
    public void ToCreatedResult_WithFailure_ReturnsProblemResult()
    {
        Result<string, NotFoundError> result = NotFoundError.Create("Item");

        var httpResult = result.ToCreatedResult("/api/items/1");

        httpResult.Should().BeOfType<ProblemHttpResult>();
    }

    #endregion

    #region ToNoContentResult

    [Fact]
    public void ToNoContentResult_WithSuccess_ReturnsNoContent()
    {
        Result<string, NotFoundError> result = "success";

        var httpResult = result.ToNoContentResult();

        httpResult.Should().BeOfType<NoContent>();
    }

    [Fact]
    public void ToNoContentResult_WithFailure_ReturnsProblemResult()
    {
        Result<string, NotFoundError> result = NotFoundError.Create("User");

        var httpResult = result.ToNoContentResult();

        httpResult.Should().BeOfType<ProblemHttpResult>();
    }

    #endregion

    #region VoidResult Extensions

    [Fact]
    public void VoidResult_ToHttpResult_WithSuccess_ReturnsNoContent()
    {
        var result = VoidResult<NotFoundError>.Success();

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<NoContent>();
    }

    [Fact]
    public void VoidResult_ToHttpResult_WithSuccess200_ReturnsOk()
    {
        var result = VoidResult<NotFoundError>.Success();

        var httpResult = result.ToHttpResult(StatusCodes.Status200OK);

        httpResult.Should().BeOfType<Ok>();
    }

    [Fact]
    public void VoidResult_ToHttpResult_WithFailure_ReturnsProblemResult()
    {
        VoidResult<NotFoundError> result = NotFoundError.Create("User");

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<ProblemHttpResult>();
    }

    #endregion
}