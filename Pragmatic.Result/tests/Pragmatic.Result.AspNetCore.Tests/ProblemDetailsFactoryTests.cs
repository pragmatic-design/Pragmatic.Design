using Pragmatic.Testing.Assertions;
using Pragmatic.Result.Http;

namespace Pragmatic.Result.AspNetCore.Tests;

public class ProblemDetailsFactoryTests
{
    #region Problem Type URL

    [Theory]
    [InlineData(400, "https://httpstatuses.io/400")]
    [InlineData(404, "https://httpstatuses.io/404")]
    [InlineData(500, "https://httpstatuses.io/500")]
    public void Create_SetsProblemTypeUrl(int statusCode, string expectedType)
    {
        var error = new TestError("TEST", statusCode);

        var problemDetails = ProblemDetailsFactory.Create(error);

        problemDetails.Type.Should().Be(expectedType);
    }

    #endregion

    private sealed record TestError(string Code, int StatusCode) : Error
    {
        public override string Code { get; } = Code;
        public override int StatusCode { get; } = StatusCode;
    }

    #region Create from Error

    [Fact]
    public void Create_FromNotFoundError_ReturnsProblemDetailsWithCorrectStatus()
    {
        var error = NotFoundError.Create("User", "123");

        var problemDetails = ProblemDetailsFactory.Create(error);

        problemDetails.Status.Should().Be(404);
        problemDetails.Title.Should().Be("Not Found");
        problemDetails.Extensions.Should().ContainKey("code");
        problemDetails.Extensions["code"].Should().Be("NOT_FOUND");
    }

    [Fact]
    public void Create_FromNotFoundError_IncludesEntityTypeAndId()
    {
        var error = NotFoundError.Create("Order", "ORD-456");

        var problemDetails = ProblemDetailsFactory.Create(error);

        problemDetails.Extensions.Should().ContainKey("entityType");
        problemDetails.Extensions["entityType"].Should().Be("Order");
        problemDetails.Extensions.Should().ContainKey("entityId");
        problemDetails.Extensions["entityId"].Should().Be("ORD-456");
    }

    [Fact]
    public void Create_FromUnauthorizedError_ReturnsProblemDetailsWithCorrectStatus()
    {
        var error = new UnauthorizedError();

        var problemDetails = ProblemDetailsFactory.Create(error);

        problemDetails.Status.Should().Be(401);
        problemDetails.Title.Should().Be("Unauthorized");
        problemDetails.Extensions["code"].Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public void Create_FromForbiddenError_ReturnsProblemDetailsWithCorrectStatus()
    {
        var error = new ForbiddenError { Resource = "admin/settings", Action = "write" };

        var problemDetails = ProblemDetailsFactory.Create(error);

        problemDetails.Status.Should().Be(403);
        problemDetails.Title.Should().Be("Forbidden");
        problemDetails.Extensions["code"].Should().Be("FORBIDDEN");
        problemDetails.Extensions["resource"].Should().Be("admin/settings");
        problemDetails.Extensions["action"].Should().Be("write");
    }

    [Fact]
    public void Create_FromConflictError_ReturnsProblemDetailsWithCorrectStatus()
    {
        var error = ConflictError.AlreadyExists("User", "john@example.com");

        var problemDetails = ProblemDetailsFactory.Create(error);

        problemDetails.Status.Should().Be(409);
        problemDetails.Title.Should().Be("Conflict");
        problemDetails.Extensions["code"].Should().Be("CONFLICT");
    }

    [Fact]
    public void Create_WithInstance_IncludesInstanceInProblemDetails()
    {
        var error = NotFoundError.Create("User", "123");

        var problemDetails = ProblemDetailsFactory.Create(error, "/api/users/123");

        problemDetails.Instance.Should().Be("/api/users/123");
    }

    #endregion

    #region Error uses StatusCode property

    [Fact]
    public void Create_FromTestError_UsesErrorStatusCode()
    {
        var error = new TestError("TEST_ERROR", 422);

        var problemDetails = ProblemDetailsFactory.Create(error);

        problemDetails.Status.Should().Be(422);
        problemDetails.Extensions["code"].Should().Be("TEST_ERROR");
    }

    [Fact]
    public void Create_FromInternalServerError_Returns500()
    {
        var error = new TestError("INTERNAL_ERROR", 500);

        var problemDetails = ProblemDetailsFactory.Create(error);

        problemDetails.Status.Should().Be(500);
        problemDetails.Title.Should().Be("Internal Server Error");
    }

    #endregion

    /// <summary>
    ///     An error that is no <c>Error</c> record writes its extensions too. This path still collected
    ///     them behind <c>error is Error</c> — the narrowing the other mapper lost long ago, and which
    ///     left a <c>ValidationError</c> naming no field.
    /// </summary>
    [Fact]
    public void Create_FromAStructError_WritesItsExtensions()
    {
        var problemDetails = ProblemDetailsFactory.Create(new StructError());

        problemDetails.Extensions["field"].Should().Be("name");
    }

    private readonly struct StructError : IError
    {
        public string Code => "STRUCT";
        public int StatusCode => 422;
        public void WriteExtensions(IDictionary<string, object?> extensions) => extensions["field"] = "name";
    }
}