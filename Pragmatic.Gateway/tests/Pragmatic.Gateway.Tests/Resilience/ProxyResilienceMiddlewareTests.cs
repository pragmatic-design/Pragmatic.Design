using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Gateway.Resilience;
using Pragmatic.Resilience.State;
using Xunit;

namespace Pragmatic.Gateway.Tests.Resilience;

public class ProxyResilienceMiddlewareTests
{
    private readonly InMemoryCircuitBreakerStateStore _stateStore = new();

    private ProxyResilienceMiddleware CreateMiddleware(GatewayResilienceOptions? options = null)
    {
        return new ProxyResilienceMiddleware(
            _stateStore,
            options ?? new GatewayResilienceOptions(),
            NullLogger<ProxyResilienceMiddleware>.Instance);
    }

    private static DefaultHttpContext CreateContext() => new();

    [Fact]
    public async Task SuccessfulRequest_RecordsSuccess()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.ExecuteWithResilienceAsync(context, ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        }, "backend-1");

        var snapshot = await _stateStore.GetSnapshotAsync("backend-1");
        snapshot.State.Should().Be(CircuitState.Closed);
        snapshot.SuccessCount.Should().Be(1);
    }

    [Fact]
    public async Task ServerError_RecordsFailure()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.ExecuteWithResilienceAsync(context, ctx =>
        {
            ctx.Response.StatusCode = 500;
            return Task.CompletedTask;
        }, "backend-1");

        var snapshot = await _stateStore.GetSnapshotAsync("backend-1");
        snapshot.FailureCount.Should().Be(1);
    }

    [Fact]
    public async Task ConsecutiveFailures_OpensCircuit()
    {
        var options = new GatewayResilienceOptions();
        options.Default.FailureThreshold = 3;
        var middleware = CreateMiddleware(options);

        for (var i = 0; i < 3; i++)
        {
            var ctx = CreateContext();
            await middleware.ExecuteWithResilienceAsync(ctx, c =>
            {
                c.Response.StatusCode = 503;
                return Task.CompletedTask;
            }, "backend-1");
        }

        var snapshot = await _stateStore.GetSnapshotAsync("backend-1");
        snapshot.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task OpenCircuit_Returns503WithRetryAfter()
    {
        var options = new GatewayResilienceOptions();
        options.Default.FailureThreshold = 1;
        options.Default.BreakDuration = TimeSpan.FromSeconds(60);
        var middleware = CreateMiddleware(options);

        // Trip the circuit
        var ctx1 = CreateContext();
        await middleware.ExecuteWithResilienceAsync(ctx1, c =>
        {
            c.Response.StatusCode = 500;
            return Task.CompletedTask;
        }, "backend-1");

        // Next request should be rejected without calling backend
        var ctx2 = CreateContext();
        var nextCalled = false;
        await middleware.ExecuteWithResilienceAsync(ctx2, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, "backend-1");

        nextCalled.Should().BeFalse();
        ctx2.Response.StatusCode.Should().Be(503);
        ctx2.Response.Headers["Retry-After"].ToString().Should().Be("60");
    }

    [Fact]
    public async Task ClientError4xx_DoesNotCountAsFailure()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.ExecuteWithResilienceAsync(context, ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        }, "backend-1");

        var snapshot = await _stateStore.GetSnapshotAsync("backend-1");
        snapshot.FailureCount.Should().Be(0);
        snapshot.SuccessCount.Should().Be(1);
    }

    [Fact]
    public async Task PerClusterIsolation_IndependentCircuits()
    {
        var options = new GatewayResilienceOptions();
        options.Default.FailureThreshold = 2;
        var middleware = CreateMiddleware(options);

        // Fail backend-1 twice → circuit opens
        for (var i = 0; i < 2; i++)
        {
            var ctx = CreateContext();
            await middleware.ExecuteWithResilienceAsync(ctx, c =>
            {
                c.Response.StatusCode = 500;
                return Task.CompletedTask;
            }, "backend-1");
        }

        // backend-2 should still work
        var ctx2 = CreateContext();
        var nextCalled = false;
        await middleware.ExecuteWithResilienceAsync(ctx2, c =>
        {
            nextCalled = true;
            c.Response.StatusCode = 200;
            return Task.CompletedTask;
        }, "backend-2");

        nextCalled.Should().BeTrue();

        var snap1 = await _stateStore.GetSnapshotAsync("backend-1");
        snap1.State.Should().Be(CircuitState.Open);

        var snap2 = await _stateStore.GetSnapshotAsync("backend-2");
        snap2.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task PerClusterConfig_OverridesDefault()
    {
        var options = new GatewayResilienceOptions();
        options.Default.FailureThreshold = 10;
        options.Clusters["backend-1"] = new ClusterResiliencePolicy { FailureThreshold = 1 };
        var middleware = CreateMiddleware(options);

        var ctx = CreateContext();
        await middleware.ExecuteWithResilienceAsync(ctx, c =>
        {
            c.Response.StatusCode = 500;
            return Task.CompletedTask;
        }, "backend-1");

        var snapshot = await _stateStore.GetSnapshotAsync("backend-1");
        snapshot.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task DisabledResilience_PassesThrough()
    {
        var options = new GatewayResilienceOptions { Enabled = false };
        var middleware = CreateMiddleware(options);
        var context = CreateContext();

        await middleware.InvokeAsync(context, ctx =>
        {
            ctx.Response.StatusCode = 500;
            return Task.CompletedTask;
        });

        // No circuit state tracking when disabled
        var snapshot = await _stateStore.GetSnapshotAsync("backend-1");
        snapshot.FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task Timeout_Returns504()
    {
        var options = new GatewayResilienceOptions();
        options.Default.Timeout = TimeSpan.FromMilliseconds(50);
        var middleware = CreateMiddleware(options);
        var context = CreateContext();

        await middleware.ExecuteWithResilienceAsync(context, async ctx =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ctx.RequestAborted).ConfigureAwait(true);
            ctx.Response.StatusCode = 200;
        }, "backend-1");

        context.Response.StatusCode.Should().Be(504);
    }

    [Fact]
    public async Task Timeout_RecordsFailureAndOpensCircuit()
    {
        var options = new GatewayResilienceOptions();
        options.Default.Timeout = TimeSpan.FromMilliseconds(50);
        options.Default.FailureThreshold = 1;
        var middleware = CreateMiddleware(options);
        var context = CreateContext();

        await middleware.ExecuteWithResilienceAsync(context, async ctx =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ctx.RequestAborted).ConfigureAwait(true);
        }, "backend-1");

        var snapshot = await _stateStore.GetSnapshotAsync("backend-1");
        snapshot.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task ConnectionException_Returns502()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.ExecuteWithResilienceAsync(context, _ =>
            throw new HttpRequestException("Connection refused"), "backend-1");

        context.Response.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task ConnectionException_RecordsFailure()
    {
        var options = new GatewayResilienceOptions();
        options.Default.FailureThreshold = 1;
        var middleware = CreateMiddleware(options);
        var context = CreateContext();

        await middleware.ExecuteWithResilienceAsync(context, _ =>
            throw new HttpRequestException("Connection refused"), "backend-1");

        var snapshot = await _stateStore.GetSnapshotAsync("backend-1");
        snapshot.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public void ResolveClusterId_WithoutFeature_ReturnsUnknown()
    {
        var context = CreateContext();
        var clusterId = ProxyResilienceMiddleware.ResolveClusterId(context);
        clusterId.Should().Be("unknown");
    }
}
