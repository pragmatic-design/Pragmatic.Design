using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Result.Http;

namespace Pragmatic.Result.AspNetCore.Tests;

public class ResultActionFilterTests
{
    private readonly ResultActionFilter _filter = new();

    #region Opt-out with SkipResultHandlingAttribute

    [Fact]
    public async Task OnResultExecutionAsync_WithSkipResultHandling_ReturnsRawResult()
    {
        Result<string, NotFoundError> result = "test value";
        var context = CreateContextWithSkipAttribute(result);

        await _filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(CreateResultExecutedContext(context))).ConfigureAwait(true);

        // Should remain ObjectResult with Result as value, not converted to OkObjectResult
        context.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)context.Result;
        objectResult.Value.Should().BeOfType<Result<string, NotFoundError>>();
    }

    #endregion

    #region DI Integration

    [Fact]
    public async Task OnResultExecutionAsync_UsesDIFactory_WhenRegistered()
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

        var context = CreateContextWithFactory(result, customFactory);

        await _filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(CreateResultExecutedContext(context))).ConfigureAwait(true);

        customFactory.Create.Received(1);

        var objectResult = (ObjectResult)context.Result;
        objectResult.Value.Should().Be(customProblemDetails);
    }

    #endregion

    #region Success Cases

    [Fact]
    public async Task OnResultExecutionAsync_WithSuccessResult_ReturnsOkObjectResult()
    {
        Result<string, NotFoundError> result = "test value";
        var context = CreateContext(result);
        var nextCalled = false;

        await _filter.OnResultExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(CreateResultExecutedContext(context));
        }).ConfigureAwait(true);

        context.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)context.Result;
        okResult.Value.Should().Be("test value");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task OnResultExecutionAsync_WithSuccessVoidResult_ReturnsNoContentResult()
    {
        var result = VoidResult<NotFoundError>.Success();
        var context = CreateContext(result);

        await _filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(CreateResultExecutedContext(context))).ConfigureAwait(true);

        context.Result.Should().BeOfType<NoContentResult>();
    }

    #endregion

    #region Failure Cases

    [Fact]
    public async Task OnResultExecutionAsync_WithNotFoundError_ReturnsProblemWith404()
    {
        Result<string, NotFoundError> result = NotFoundError.Create("User", "123");
        var context = CreateContext(result);

        await _filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(CreateResultExecutedContext(context))).ConfigureAwait(true);

        context.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)context.Result;
        objectResult.StatusCode.Should().Be(404);
        objectResult.Value.Should().BeOfType<ProblemDetails>();
    }

    [Fact]
    public async Task OnResultExecutionAsync_WithUnauthorizedError_ReturnsProblemWith401()
    {
        Result<string, UnauthorizedError> result = new UnauthorizedError();
        var context = CreateContext(result);

        await _filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(CreateResultExecutedContext(context))).ConfigureAwait(true);

        context.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)context.Result;
        objectResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task OnResultExecutionAsync_WithForbiddenError_ReturnsProblemWith403()
    {
        Result<string, ForbiddenError> result = ForbiddenError.Create("admin", "delete");
        var context = CreateContext(result);

        await _filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(CreateResultExecutedContext(context))).ConfigureAwait(true);

        context.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)context.Result;
        objectResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task OnResultExecutionAsync_WithConflictError_ReturnsProblemWith409()
    {
        Result<string, ConflictError> result = ConflictError.AlreadyExists("User", "email");
        var context = CreateContext(result);

        await _filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(CreateResultExecutedContext(context))).ConfigureAwait(true);

        context.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)context.Result;
        objectResult.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task OnResultExecutionAsync_WithVoidResultFailure_ReturnsProblemDetails()
    {
        VoidResult<NotFoundError> result = NotFoundError.Create("User", "123");
        var context = CreateContext(result);

        await _filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(CreateResultExecutedContext(context))).ConfigureAwait(true);

        context.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)context.Result;
        objectResult.StatusCode.Should().Be(404);
    }

    #endregion

    #region Non-Result Types

    [Fact]
    public async Task OnResultExecutionAsync_WithNonResultType_PassesThroughUnchanged()
    {
        var originalResult = new OkObjectResult("plain string");
        var context = CreateContextWithResult(originalResult);

        await _filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(CreateResultExecutedContext(context))).ConfigureAwait(true);

        context.Result.Should().BeSameAs(originalResult);
    }

    [Fact]
    public async Task OnResultExecutionAsync_WithStatusCodeResult_PassesThroughUnchanged()
    {
        var originalResult = new StatusCodeResult(200);
        var context = CreateContextWithResult(originalResult);

        await _filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(CreateResultExecutedContext(context))).ConfigureAwait(true);

        context.Result.Should().BeSameAs(originalResult);
    }

    #endregion

    #region Helpers

    private static ResultExecutingContext CreateContext(object resultValue)
    {
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new ObjectResult(resultValue),
            null!);
    }

    private static ResultExecutingContext CreateContextWithResult(IActionResult result)
    {
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            result,
            null!);
    }

    private static ResultExecutingContext CreateContextWithSkipAttribute(object resultValue)
    {
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        var actionDescriptor = new ActionDescriptor
        {
            EndpointMetadata = new List<object> { new SkipResultHandlingAttribute() }
        };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            actionDescriptor);

        return new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new ObjectResult(resultValue),
            null!);
    }

    private static ResultExecutingContext CreateContextWithFactory(object resultValue, IProblemDetailsFactory factory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(factory);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new ObjectResult(resultValue),
            null!);
    }

    private static ResultExecutedContext CreateResultExecutedContext(ResultExecutingContext executingContext)
    {
        return new ResultExecutedContext(
            executingContext,
            new List<IFilterMetadata>(),
            executingContext.Result,
            null!);
    }

    #endregion
}