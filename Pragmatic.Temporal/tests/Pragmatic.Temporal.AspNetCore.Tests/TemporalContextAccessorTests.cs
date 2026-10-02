using Microsoft.AspNetCore.Http;
using Pragmatic.Temporal.AspNetCore.Middleware;
using Pragmatic.Temporal.Context;
using Pragmatic.Temporal.Testing;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.AspNetCore.Tests;

public class TemporalContextAccessorTests
{
    [Fact]
    public void Context_InsideRequest_ReturnsTheMiddlewareContext()
    {
        var requestContext = TestTemporalContext.ForRome();
        var httpContext = new DefaultHttpContext();
        httpContext.Items[typeof(TemporalContext)] = requestContext;

        var accessor = new TemporalContextAccessor(
            new HttpContextAccessor { HttpContext = httpContext },
            new TemporalOptions(),
            new TestClock());

        Assert.Same(requestContext, accessor.Context);
    }

    [Fact]
    public void Context_OutsideRequest_FallsBackToOptionsIncludingPolicies()
    {
        var options = new TemporalOptions
        {
            NonExistentTimeHandling = NonExistentTimePolicy.ThrowException,
            AmbiguousTimeHandling = AmbiguousTimePolicy.ThrowException,
            FirstDayOfWeek = DayOfWeek.Sunday,
            DefaultCountryCode = "IT"
        };

        var accessor = new TemporalContextAccessor(
            new HttpContextAccessor { HttpContext = null },
            options,
            new TestClock());

        var context = accessor.Context;

        Assert.Equal(NonExistentTimePolicy.ThrowException, context.NonExistentTimeHandling);
        Assert.Equal(AmbiguousTimePolicy.ThrowException, context.AmbiguousTimeHandling);
        Assert.Equal(DayOfWeek.Sunday, context.FirstDayOfWeek);
        Assert.Equal("IT", context.DefaultCountryCode);
    }
}
