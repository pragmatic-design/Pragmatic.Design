using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Result.Http;

namespace Pragmatic.Result.AspNetCore.Tests;

public class ResultEndpointFilterTests
{
    private readonly ResultEndpointFilter _filter = new();

    #region Opt-out with SkipResultHandlingAttribute

    [Fact]
    public async Task InvokeAsync_WithSkipResultHandling_ReturnsRawResult()
    {
        Result<string, NotFoundError> result = "test value";
        var context = CreateContextWithSkipAttribute();
        var next = CreateNext(result);

        var httpResult = await _filter.InvokeAsync(context, next).ConfigureAwait(true);

        // Should return raw Result, not converted
        httpResult.Should().Be(result);
    }

    #endregion

    #region DI Integration

    [Fact]
    public async Task InvokeAsync_UsesDIFactory_WhenRegistered()
    {
        Result<string, NotFoundError> result = NotFoundError.Create("User", "123");
        var customFactory = new ProblemDetailsFactoryMock();
        var customProblemDetails = new ProblemDetails
        {
            Status = 404,
            Title = "Custom Title",
            Detail = "Custom detail from DI"
        };
        customFactory.Create.Returns(customProblemDetails);

        var context = CreateContextWithFactory(customFactory);
        var next = CreateNext(result);

        await _filter.InvokeAsync(context, next).ConfigureAwait(true);

        customFactory.Create.Received(1);
    }

    #endregion

    #region Success Cases

    [Fact]
    public async Task InvokeAsync_WithSuccessResult_ReturnsOkWithValue()
    {
        Result<string, NotFoundError> result = "test value";
        var context = CreateContext();
        var next = CreateNext(result);

        var httpResult = await _filter.InvokeAsync(context, next).ConfigureAwait(true);

        // Note: Results.Ok(object) returns Ok<object> since ValueAsObject is typed as object
        httpResult.Should().BeOfType<Ok<object>>();
        var ok = (Ok<object>)httpResult!;
        ok.Value.Should().Be("test value");
    }

    [Fact]
    public async Task InvokeAsync_WithSuccessVoidResult_ReturnsNoContent()
    {
        var result = VoidResult<NotFoundError>.Success();
        var context = CreateContext();
        var next = CreateNext(result);

        var httpResult = await _filter.InvokeAsync(context, next).ConfigureAwait(true);

        httpResult.Should().BeOfType<NoContent>();
    }

    #endregion

    #region Failure Cases

    [Fact]
    public async Task InvokeAsync_WithNotFoundError_ReturnsProblemWith404()
    {
        Result<string, NotFoundError> result = NotFoundError.Create("User", "123");
        var context = CreateContext();
        var next = CreateNext(result);

        var httpResult = await _filter.InvokeAsync(context, next).ConfigureAwait(true);

        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)httpResult!;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task InvokeAsync_WithUnauthorizedError_ReturnsProblemWith401()
    {
        Result<string, UnauthorizedError> result = new UnauthorizedError();
        var context = CreateContext();
        var next = CreateNext(result);

        var httpResult = await _filter.InvokeAsync(context, next).ConfigureAwait(true);

        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)httpResult!;
        problem.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_WithForbiddenError_ReturnsProblemWith403()
    {
        Result<string, ForbiddenError> result = ForbiddenError.Create("admin", "delete");
        var context = CreateContext();
        var next = CreateNext(result);

        var httpResult = await _filter.InvokeAsync(context, next).ConfigureAwait(true);

        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)httpResult!;
        problem.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_WithConflictError_ReturnsProblemWith409()
    {
        Result<string, ConflictError> result = ConflictError.AlreadyExists("User", "email");
        var context = CreateContext();
        var next = CreateNext(result);

        var httpResult = await _filter.InvokeAsync(context, next).ConfigureAwait(true);

        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)httpResult!;
        problem.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task InvokeAsync_WithVoidResultFailure_ReturnsProblemDetails()
    {
        VoidResult<NotFoundError> result = NotFoundError.Create("User", "123");
        var context = CreateContext();
        var next = CreateNext(result);

        var httpResult = await _filter.InvokeAsync(context, next).ConfigureAwait(true);

        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)httpResult!;
        problem.StatusCode.Should().Be(404);
    }

    #endregion

    #region Non-Result Types

    [Fact]
    public async Task InvokeAsync_WithNonResultType_PassesThroughUnchanged()
    {
        var originalResult = "plain string";
        var context = CreateContext();
        var next = CreateNext(originalResult);

        var httpResult = await _filter.InvokeAsync(context, next).ConfigureAwait(true);

        httpResult.Should().Be(originalResult);
    }

    [Fact]
    public async Task InvokeAsync_WithNull_PassesThroughUnchanged()
    {
        var context = CreateContext();
        var next = CreateNext(null);

        var httpResult = await _filter.InvokeAsync(context, next).ConfigureAwait(true);

        httpResult.Should().BeNull();
    }

    #endregion

    #region Helpers

    private static EndpointFilterInvocationContext CreateContext()
    {
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        httpContext.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(), "test"));
        return new DefaultEndpointFilterInvocationContext(httpContext);
    }

    private static EndpointFilterInvocationContext CreateContextWithSkipAttribute()
    {
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        httpContext.SetEndpoint(new Endpoint(
            null,
            new EndpointMetadataCollection(new SkipResultHandlingAttribute()),
            "test"));
        return new DefaultEndpointFilterInvocationContext(httpContext);
    }

    private static EndpointFilterInvocationContext CreateContextWithFactory(IProblemDetailsFactory factory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(factory);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        httpContext.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(), "test"));
        return new DefaultEndpointFilterInvocationContext(httpContext);
    }

    private static EndpointFilterDelegate CreateNext(object? result)
    {
        return _ => new ValueTask<object?>(result);
    }

    #endregion
}