using Pragmatic.Temporal.AspNetCore.Detection;

namespace Pragmatic.Temporal.AspNetCore.Tests;

public class TemporalAspNetCoreOptionsTests
{
    [Fact]
    public void PropagateOptionsToStrategies_ReachesEveryDefaultStrategy()
    {
        var options = new TemporalAspNetCoreOptions
        {
            TimezoneHeader = "X-My-TZ",
            TimezoneClaim = "my-timezone",
            TimezoneQueryParameter = "zone",
            TimezoneCookie = "zone-cookie"
        };

        options.PropagateOptionsToStrategies();

        Assert.Equal("X-My-TZ", options.DetectionStrategies.OfType<HeaderTimeZoneStrategy>().Single().HeaderName);
        Assert.Equal("my-timezone", options.DetectionStrategies.OfType<ClaimsTimeZoneStrategy>().Single().ClaimType);
        Assert.Equal("zone", options.DetectionStrategies.OfType<QueryStringTimeZoneStrategy>().Single().QueryParameter);
        Assert.Equal("zone-cookie", options.DetectionStrategies.OfType<CookieTimeZoneStrategy>().Single().CookieName);
    }

    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new TemporalAspNetCoreOptions();

        Assert.Equal("X-Timezone", options.TimezoneHeader);
        Assert.Equal("timezone", options.TimezoneClaim);
        Assert.Equal("tz", options.TimezoneQueryParameter);
        Assert.Equal("tz", options.TimezoneCookie);
        Assert.Equal(4, options.DetectionStrategies.Count);
    }
}
