using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Composition.Hosting;
using Pragmatic.Maintenance;

namespace Pragmatic.Composition.Tests.Hosting;

public class MaintenanceMiddlewareTests
{
    private static MaintenanceModeOptions DefaultOptions => new();

    private static RequestDelegate NextReturnsOk => context =>
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        return Task.CompletedTask;
    };

    [Fact]
    public async Task InvokeAsync_NotActive_PassesThrough()
    {
        var maintenanceMode = new MaintenanceModeMock();
        maintenanceMode.IsActive.Returns(false);

        var middleware = new MaintenanceMiddleware(NextReturnsOk, maintenanceMode, DefaultOptions);
        var context = CreateHttpContext("/api/users");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_Active_Returns503()
    {
        var maintenanceMode = new MaintenanceModeMock();
        maintenanceMode.IsActive.Returns(true);
        maintenanceMode.Reason.Returns("Database migration");

        var middleware = new MaintenanceMiddleware(NextReturnsOk, maintenanceMode, DefaultOptions);
        var context = CreateHttpContext("/api/users");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task InvokeAsync_Active_AdminPath_PassesThrough()
    {
        var maintenanceMode = new MaintenanceModeMock();
        maintenanceMode.IsActive.Returns(true);

        var middleware = new MaintenanceMiddleware(NextReturnsOk, maintenanceMode, DefaultOptions);
        var context = CreateHttpContext("/admin/maintenance/panel");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_Active_AdminStatusEndpoint_PassesThrough()
    {
        var maintenanceMode = new MaintenanceModeMock();
        maintenanceMode.IsActive.Returns(true);

        var middleware = new MaintenanceMiddleware(NextReturnsOk, maintenanceMode, DefaultOptions);
        var context = CreateHttpContext("/admin/maintenance");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_Active_CustomAdminPath_PassesThrough()
    {
        var maintenanceMode = new MaintenanceModeMock();
        maintenanceMode.IsActive.Returns(true);
        var options = new MaintenanceModeOptions { AdminPath = "/_ops/maint" };

        var middleware = new MaintenanceMiddleware(NextReturnsOk, maintenanceMode, options);
        var context = CreateHttpContext("/_ops/maint/panel");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_Active_WithEstimatedEnd_SetsRetryAfterHeader()
    {
        var maintenanceMode = new MaintenanceModeMock();
        maintenanceMode.IsActive.Returns(true);
        maintenanceMode.EstimatedEnd.Returns(DateTimeOffset.UtcNow.AddMinutes(2));

        var middleware = new MaintenanceMiddleware(NextReturnsOk, maintenanceMode, DefaultOptions);
        var context = CreateHttpContext("/api/data");

        await middleware.InvokeAsync(context);

        context.Response.Headers["Retry-After"].ToString().Should().NotBeNullOrEmpty();
        int.Parse(context.Response.Headers["Retry-After"].ToString()).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task InvokeAsync_Active_WithoutEstimatedEnd_NoRetryAfterHeader()
    {
        var maintenanceMode = new MaintenanceModeMock();
        maintenanceMode.IsActive.Returns(true);
        maintenanceMode.EstimatedEnd.Returns((DateTimeOffset?)null);

        var middleware = new MaintenanceMiddleware(NextReturnsOk, maintenanceMode, DefaultOptions);
        var context = CreateHttpContext("/api/data");

        await middleware.InvokeAsync(context);

        context.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Active_HealthEndpoint_Returns503()
    {
        var maintenanceMode = new MaintenanceModeMock();
        maintenanceMode.IsActive.Returns(true);

        var middleware = new MaintenanceMiddleware(NextReturnsOk, maintenanceMode, DefaultOptions);
        var context = CreateHttpContext("/health");

        await middleware.InvokeAsync(context);

        // /health is NOT an admin path, so it gets blocked
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task InvokeAsync_Active_AdminPathCaseInsensitive_PassesThrough()
    {
        var maintenanceMode = new MaintenanceModeMock();
        maintenanceMode.IsActive.Returns(true);

        var middleware = new MaintenanceMiddleware(NextReturnsOk, maintenanceMode, DefaultOptions);
        var context = CreateHttpContext("/Admin/Maintenance/Panel");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    private static HttpContext CreateHttpContext(string path, string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        // Use a real MemoryStream for the response body so WriteAsync works
        context.Response.Body = new MemoryStream();
        return context;
    }
}
