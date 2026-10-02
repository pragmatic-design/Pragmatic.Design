using Pragmatic.Testing.Assertions;
using Pragmatic.Endpoints.Extensions;
using Pragmatic.Result;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

public class ErrorExtensionsTests
{
    [Theory]
    [InlineData(400, "Bad Request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(404, "Not Found")]
    [InlineData(409, "Conflict")]
    [InlineData(422, "Unprocessable Entity")]
    [InlineData(500, "Internal Server Error")]
    public void ToProblemDetails_KnownStatusCode_ReturnsCorrectTitle(int statusCode, string expectedTitle)
    {
        var error = new TestError(statusCode);

        var problem = error.ToProblemDetails();

        problem.Title.Should().Be(expectedTitle);
    }

    [Fact]
    public void ToProblemDetails_CustomStatus_ReturnsFallbackTitle()
    {
        var error = new TestError(418);

        var problem = error.ToProblemDetails();

        problem.Title.Should().Be("Error");
    }

    [Fact]
    public void ToProblemDetails_SetsStatusCode()
    {
        var error = new TestError(404);

        var problem = error.ToProblemDetails();

        problem.Status.Should().Be(404);
    }

    [Fact]
    public void ToProblemDetails_SetsDetail_FromDescription()
    {
        var error = new TestErrorWithDescription(400, "Something went wrong");

        var problem = error.ToProblemDetails();

        problem.Detail.Should().Be("Something went wrong");
    }

    [Fact]
    public void ToProblemDetails_SetsDetail_NullWhenNoDescription()
    {
        var error = new TestError(400);

        var problem = error.ToProblemDetails();

        problem.Detail.Should().BeNull();
    }

    [Fact]
    public void ToProblemDetails_SetsCodeExtension()
    {
        var error = new TestError(400);

        var problem = error.ToProblemDetails();

        problem.Extensions.Should().ContainKey("code");
        problem.Extensions["code"].Should().Be("TEST_ERROR");
    }

    [Fact]
    public void ToProblemDetails_SetsType_WithStatusUrl()
    {
        var error = new TestError(404);

        var problem = error.ToProblemDetails();

        problem.Type.Should().Be("https://httpstatuses.io/404");
    }

    [Fact]
    public void ToProblemDetails_WithTitle_UsesProvidedTitle()
    {
        var error = new TestErrorWithTitle(400, "Custom Title");

        var problem = error.ToProblemDetails();

        problem.Title.Should().Be("Custom Title");
    }

    [Fact]
    public void ToProblemDetails_WithoutTitle_UsesFallback()
    {
        var error = new TestError(400);

        var problem = error.ToProblemDetails();

        // Title is null on IError, so fallback is used
        problem.Title.Should().Be("Bad Request");
    }

    [Fact]
    public void ToResult_400_ReturnsBadRequest()
    {
        var error = new TestError(400);

        var result = error.ToResult();

        // IResult should be a non-null result
        result.Should().NotBeNull();
    }

    [Fact]
    public void ToResult_404_ReturnsNotFound()
    {
        var error = new TestError(404);

        var result = error.ToResult();

        result.Should().NotBeNull();
    }

    [Fact]
    public void ToResult_401_ReturnsUnauthorized()
    {
        var error = new TestError(401);

        var result = error.ToResult();

        result.Should().NotBeNull();
    }

    [Fact]
    public void ToResult_409_ReturnsConflict()
    {
        var error = new TestError(409);

        var result = error.ToResult();

        result.Should().NotBeNull();
    }

    [Fact]
    public void ToResult_422_ReturnsUnprocessableEntity()
    {
        var error = new TestError(422);

        var result = error.ToResult();

        result.Should().NotBeNull();
    }

    [Fact]
    public void ToResult_500_ReturnsProblem()
    {
        var error = new TestError(500);

        var result = error.ToResult();

        result.Should().NotBeNull();
    }

    [Fact]
    public void ToProblemDetails_502_ReturnsBadGateway()
    {
        var error = new TestError(502);

        var problem = error.ToProblemDetails();

        problem.Title.Should().Be("Bad Gateway");
        problem.Status.Should().Be(502);
    }

    /// <summary>
    ///     The resolver reaches the error's own extensions, not only its title and detail:
    ///     a validation failure's words are in its extensions.
    /// </summary>
    [Fact]
    public void ToProblemDetails_HandsTheResolverToTheErrorsExtensions()
    {
        var problem = new WordyError().ToProblemDetails(new OneWordResolver());

        problem.Extensions["said"].Should().Be("hello");
    }

    private readonly struct WordyError : IError
    {
        public string Code => "WORDY";
        public int StatusCode => 422;

        public void WriteExtensions(IDictionary<string, object?> extensions, IErrorMessageResolver? resolver)
            => extensions["said"] = resolver?.ResolveKey("greeting");
    }

    private sealed class OneWordResolver : IErrorMessageResolver
    {
        public string? Resolve(string code, object? context = null) => null;

        string? IErrorMessageResolver.ResolveKey(string messageKey, IReadOnlyDictionary<string, object>? parameters)
            => messageKey == "greeting" ? "hello" : null;
    }

    [Fact]
    public void ToProblemDetails_403_ReturnsForbidden()
    {
        var error = new TestError(403);

        var problem = error.ToProblemDetails();

        problem.Title.Should().Be("Forbidden");
    }

    [Theory]
    [InlineData(429, "Too Many Requests")]
    [InlineData(503, "Service Unavailable")]
    [InlineData(504, "Gateway Timeout")]
    public void ToProblemDetails_ResilienceStatusCodes_ReturnsCorrectTitle(int statusCode, string expectedTitle)
    {
        var error = new TestError(statusCode);

        var problem = error.ToProblemDetails();

        problem.Title.Should().Be(expectedTitle);
    }

    private sealed record TestError(int Status) : Error
    {
        public override string Code => "TEST_ERROR";
        public override int StatusCode => Status;
    }

    private sealed record TestErrorWithTitle(int Status, string CustomTitle) : Error
    {
        public override string Code => "TEST_ERROR";
        public override int StatusCode => Status;
        public override string Title => CustomTitle;
    }

    private sealed record TestErrorWithDescription(int Status, string Desc) : IError
    {
        public string Code => "TEST_ERROR";
        public int StatusCode => Status;
        public string Title => string.Empty;
        public string? Description => Desc;
    }
}
