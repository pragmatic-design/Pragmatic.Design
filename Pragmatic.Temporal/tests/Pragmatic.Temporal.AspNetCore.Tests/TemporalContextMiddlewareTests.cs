using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Temporal.AspNetCore.Middleware;
using Pragmatic.Temporal.Context;
using Pragmatic.Temporal.Extensions;

namespace Pragmatic.Temporal.AspNetCore.Tests;

public class TemporalContextMiddlewareTests
{
    private static DefaultHttpContext CreateHttpContext(
        Action<TemporalOptions>? configureTemporal = null,
        TemporalAspNetCoreOptions? aspOptions = null)
    {
        var services = new ServiceCollection();
        services.AddPragmaticTemporal(configureTemporal);
        services.AddSingleton(Options.Create(aspOptions ?? new TemporalAspNetCoreOptions()));

        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    [Fact]
    public async Task Invoke_WithTimezoneHeader_SetsClientZoneInContextItems()
    {
        var context = CreateHttpContext();
        context.Request.Headers["X-Timezone"] = "Europe/Rome";

        var middleware = new TemporalContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        var temporal = Assert.IsType<TemporalContext>(context.Items[typeof(TemporalContext)]);
        Assert.Contains("Rome", temporal.ClientTimeZone.Id, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoke_QueryStringBeatsHeader_ByPriority()
    {
        var context = CreateHttpContext();
        context.Request.QueryString = new QueryString("?tz=Europe/London");
        context.Request.Headers["X-Timezone"] = "Europe/Rome";

        var middleware = new TemporalContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        var temporal = Assert.IsType<TemporalContext>(context.Items[typeof(TemporalContext)]);
        Assert.Contains("London", temporal.ClientTimeZone.Id, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoke_NoTimezoneSupplied_FallsBackToDefault()
    {
        var context = CreateHttpContext();

        var middleware = new TemporalContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        var temporal = Assert.IsType<TemporalContext>(context.Items[typeof(TemporalContext)]);
        Assert.Equal(TimeZoneInfo.Utc.Id, temporal.ClientTimeZone.Id);
    }

    [Fact]
    public async Task Invoke_InvalidTimezoneWithThrowEnabled_Throws()
    {
        var context = CreateHttpContext(o => o.ThrowOnInvalidTimeZone = true);
        context.Request.Headers["X-Timezone"] = "Not/AZone";

        var middleware = new TemporalContextMiddleware(_ => Task.CompletedTask);

        await Assert.ThrowsAsync<TimeZoneNotFoundException>(() => middleware.InvokeAsync(context));
    }

    [Fact]
    public async Task Invoke_InvalidTimezoneWithoutThrow_FallsBackToDefault()
    {
        var context = CreateHttpContext();
        context.Request.Headers["X-Timezone"] = "Not/AZone";

        var middleware = new TemporalContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        var temporal = Assert.IsType<TemporalContext>(context.Items[typeof(TemporalContext)]);
        Assert.Equal(TimeZoneInfo.Utc.Id, temporal.ClientTimeZone.Id);
    }

    [Fact]
    public async Task Invoke_PropagatesPolicyDefaultsFromOptions()
    {
        var context = CreateHttpContext(o =>
        {
            o.FirstDayOfWeek = DayOfWeek.Sunday;
            o.DefaultCountryCode = "IT";
        });

        var middleware = new TemporalContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        var temporal = Assert.IsType<TemporalContext>(context.Items[typeof(TemporalContext)]);
        Assert.Equal(DayOfWeek.Sunday, temporal.FirstDayOfWeek);
        Assert.Equal("IT", temporal.DefaultCountryCode);
    }

    [Fact]
    public async Task Invoke_ExposesAmbientContextDuringRequest_AndClearsItAfter()
    {
        var context = CreateHttpContext();
        TemporalContext? observedDuringRequest = null;

        var middleware = new TemporalContextMiddleware(_ =>
        {
            observedDuringRequest = TemporalContextHolder.Current;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.NotNull(observedDuringRequest);
        Assert.Same(context.Items[typeof(TemporalContext)], observedDuringRequest);
        Assert.Null(TemporalContextHolder.Current);
    }

    [Fact]
    public async Task Invoke_ClearsAmbientContext_EvenWhenPipelineThrows()
    {
        var context = CreateHttpContext();
        var middleware = new TemporalContextMiddleware(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Null(TemporalContextHolder.Current);
    }
}
