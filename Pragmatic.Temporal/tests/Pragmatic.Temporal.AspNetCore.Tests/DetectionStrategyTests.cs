using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Pragmatic.Temporal.AspNetCore.Detection;

namespace Pragmatic.Temporal.AspNetCore.Tests;

public class DetectionStrategyTests
{
    [Fact]
    public void HeaderStrategy_WithValidTimezone_DetectsZone()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Timezone"] = "Europe/Rome";

        var zone = new HeaderTimeZoneStrategy().Detect(context);

        Assert.NotNull(zone);
    }

    [Fact]
    public void HeaderStrategy_WithInvalidTimezone_ReturnsNullButExposesRawValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Timezone"] = "Not/AZone";
        var strategy = new HeaderTimeZoneStrategy();

        Assert.Null(strategy.Detect(context));
        Assert.Equal("Not/AZone", strategy.GetRawValue(context));
    }

    [Fact]
    public void HeaderStrategy_WithOverlongValue_IsRejected()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Timezone"] = new string('a', 65);

        Assert.Null(new HeaderTimeZoneStrategy().Detect(context));
    }

    [Fact]
    public void HeaderStrategy_CustomHeaderName_IsHonored()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Custom-TZ"] = "Europe/Rome";

        var strategy = new HeaderTimeZoneStrategy { HeaderName = "X-Custom-TZ" };

        Assert.NotNull(strategy.Detect(context));
    }

    [Fact]
    public void QueryStringStrategy_WithValidTimezone_DetectsZone()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tz=Europe/Rome");

        Assert.NotNull(new QueryStringTimeZoneStrategy().Detect(context));
    }

    [Fact]
    public void QueryStringStrategy_MissingParameter_ReturnsNullAndNoRawValue()
    {
        var context = new DefaultHttpContext();
        var strategy = new QueryStringTimeZoneStrategy();

        Assert.Null(strategy.Detect(context));
        Assert.Null(strategy.GetRawValue(context));
    }

    [Fact]
    public void CookieStrategy_WithValidTimezone_DetectsZone()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "tz=Europe/Rome";

        Assert.NotNull(new CookieTimeZoneStrategy().Detect(context));
    }

    [Fact]
    public void CookieStrategy_CustomCookieName_IsHonored()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "my-tz=Europe/Rome";

        var strategy = new CookieTimeZoneStrategy { CookieName = "my-tz" };

        Assert.NotNull(strategy.Detect(context));
    }

    [Fact]
    public void ClaimsStrategy_WithTimezoneClaim_DetectsZone()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("timezone", "Europe/Rome")], "test"))
        };

        Assert.NotNull(new ClaimsTimeZoneStrategy().Detect(context));
    }

    [Fact]
    public void ClaimsStrategy_UnauthenticatedUser_ReturnsNull()
    {
        var context = new DefaultHttpContext();

        Assert.Null(new ClaimsTimeZoneStrategy().Detect(context));
    }

    [Fact]
    public void Priorities_OrderQueryBeforeHeaderBeforeClaimsBeforeCookie()
    {
        Assert.True(new QueryStringTimeZoneStrategy().Priority < new HeaderTimeZoneStrategy().Priority);
        Assert.True(new HeaderTimeZoneStrategy().Priority < new ClaimsTimeZoneStrategy().Priority);
        Assert.True(new ClaimsTimeZoneStrategy().Priority < new CookieTimeZoneStrategy().Priority);
    }
}
