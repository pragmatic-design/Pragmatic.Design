using System.Reflection;
using System.Text;
using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Gateway.Maintenance;
using Xunit;

namespace Pragmatic.Gateway.Tests.Maintenance;

/// <summary>
///     HTTP-pipeline unit tests for <see cref="MaintenanceMiddleware" /> using an in-memory
///     <see cref="DefaultHttpContext" /> — no live YARP host or backend required.
/// </summary>
public sealed class MaintenanceMiddlewareTests
{
    private static readonly MethodInfo OnKvChanged =
        typeof(MaintenanceState).GetMethod("OnKvChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static void Set(MaintenanceState state, string key, string? value, bool deleted = false) =>
        OnKvChanged.Invoke(state, [new KvChangedPayload { Key = key, Value = value, Version = 1, Deleted = deleted }]);

    private static (DefaultHttpContext Context, MemoryStream Body) CreateContext()
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        return (context, body);
    }

    private static MaintenanceMiddleware Create(
        RequestDelegate next,
        MaintenanceState state,
        GatewayOptions? options = null) =>
        new(next, state, new MaintenancePage(options ?? new GatewayOptions()));

    [Fact]
    public async Task InvokeAsync_HealthEndpoint_BypassesFullMaintenance()
    {
        // GW-M1 regression: the health endpoint must stay reachable during maintenance so an
        // orchestrator liveness probe does not get a 503 and restart the gateway.
        var state = new MaintenanceState();
        Set(state, "state/gateway/maintenance", "true"); // full maintenance
        var nextCalled = false;
        var middleware = Create(_ => { nextCalled = true; return Task.CompletedTask; }, state);
        var (context, _) = CreateContext();
        context.Request.Path = "/health";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue("the health endpoint must bypass maintenance");
        context.Response.StatusCode.Should().NotBe(503);
    }

    [Fact]
    public async Task InvokeAsync_NoMaintenance_CallsNext()
    {
        var state = new MaintenanceState();
        var nextCalled = false;
        var middleware = Create(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, state);
        var (context, _) = CreateContext();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_FullMaintenance_Returns503AndDoesNotCallNext()
    {
        var state = new MaintenanceState();
        Set(state, "state/gateway/maintenance", "true");
        var nextCalled = false;
        var middleware = Create(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, state);
        var (context, _) = CreateContext();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task InvokeAsync_FullMaintenance_SetsRetryAfterAndContentType()
    {
        var state = new MaintenanceState();
        Set(state, "state/gateway/maintenance", "true");
        var middleware = Create(_ => Task.CompletedTask, state);
        var (context, _) = CreateContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers["Retry-After"].ToString().Should().Be("60");
        context.Response.ContentType.Should().Be("text/html; charset=utf-8");
    }

    [Fact]
    public async Task InvokeAsync_FullMaintenance_WritesDefaultHtmlPage()
    {
        var state = new MaintenanceState();
        Set(state, "state/gateway/maintenance", "true");
        var middleware = Create(_ => Task.CompletedTask, state);
        var (context, body) = CreateContext();

        await middleware.InvokeAsync(context);

        var html = Encoding.UTF8.GetString(body.ToArray());
        html.Should().Contain("Under Maintenance");
    }

    [Fact]
    public async Task InvokeAsync_PerAppMaintenance_EarlyMiddlewarePassesThrough()
    {
        // GW-M3: per-app maintenance is NOT decided by this early middleware (the target app is unknown
        // before routing) — it is decided in the reverse-proxy pipeline. So a per-app-only maintenance
        // signal must pass through here; only full-gateway maintenance short-circuits.
        var state = new MaintenanceState();
        Set(state, "state/app:booking/i-1",
            """{"appId":"booking","appName":"booking","instanceId":"i-1","state":"Maintenance"}""");
        state.IsInMaintenance("booking").Should().BeTrue("the signal is there, and still not decided here");
        var nextCalled = false;
        var middleware = Create(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, state);
        var (context, _) = CreateContext();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_CustomPageConfigured_ServesCustomBytes()
    {
        var customPath = Path.Combine(Path.GetTempPath(), $"maint-{Guid.NewGuid():N}.html");
        const string customHtml = "<html><body>Custom Maintenance Page</body></html>";
        await File.WriteAllTextAsync(customPath, customHtml);
        try
        {
            var state = new MaintenanceState();
            Set(state, "state/gateway/maintenance", "true");
            var middleware = Create(_ => Task.CompletedTask, state, new GatewayOptions { MaintenancePagePath = customPath });
            var (context, body) = CreateContext();

            await middleware.InvokeAsync(context);

            var served = Encoding.UTF8.GetString(body.ToArray());
            served.Should().Be(customHtml);
        }
        finally
        {
            File.Delete(customPath);
        }
    }

    [Fact]
    public async Task InvokeAsync_CustomPagePathMissing_FallsBackToDefault()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.html");
        var state = new MaintenanceState();
        Set(state, "state/gateway/maintenance", "true");
        var middleware = Create(_ => Task.CompletedTask, state, new GatewayOptions { MaintenancePagePath = missingPath });
        var (context, body) = CreateContext();

        await middleware.InvokeAsync(context);

        var html = Encoding.UTF8.GetString(body.ToArray());
        html.Should().Contain("Under Maintenance");
        context.Response.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task InvokeAsync_AfterFullMaintenanceCleared_CallsNextAgain()
    {
        var state = new MaintenanceState();
        Set(state, "state/gateway/maintenance", "true");
        var nextCalled = false;
        var middleware = Create(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, state);

        var (blocked, _) = CreateContext();
        await middleware.InvokeAsync(blocked);
        blocked.Response.StatusCode.Should().Be(503);

        Set(state, "state/gateway/maintenance", "false");
        var (allowed, _) = CreateContext();
        await middleware.InvokeAsync(allowed);

        nextCalled.Should().BeTrue();
        allowed.Response.StatusCode.Should().Be(200);
    }
}
