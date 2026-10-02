using Pragmatic.Temporal.AspNetCore.Detection;

namespace Pragmatic.Temporal.AspNetCore;

/// <summary>
///     ASP.NET Core specific options for Pragmatic.Temporal.
/// </summary>
public sealed class TemporalAspNetCoreOptions
{
    /// <summary>
    ///     The header name to read client timezone from.
    ///     Default: X-Timezone
    /// </summary>
    public string TimezoneHeader { get; set; } = "X-Timezone";

    /// <summary>
    ///     The claim type to read timezone from.
    ///     Default: timezone
    /// </summary>
    public string TimezoneClaim { get; set; } = "timezone";

    /// <summary>
    ///     The query parameter to read timezone from.
    ///     Default: tz
    /// </summary>
    public string TimezoneQueryParameter { get; set; } = "tz";

    /// <summary>
    ///     The cookie name to read timezone from.
    ///     Default: tz
    /// </summary>
    public string TimezoneCookie { get; set; } = "tz";

    /// <summary>
    ///     Timezone detection strategies in priority order.
    /// </summary>
    public List<ITimeZoneDetectionStrategy> DetectionStrategies { get; set; } = new()
    {
        new QueryStringTimeZoneStrategy(),
        new HeaderTimeZoneStrategy(),
        new ClaimsTimeZoneStrategy(),
        new CookieTimeZoneStrategy()
    };

    /// <summary>
    ///     Propagates the configured option values (header name, claim type, query parameter,
    ///     cookie name) to the default detection strategies. Called automatically by
    ///     <c>AddPragmaticTemporalAspNetCore</c>; call it manually only when configuring
    ///     options outside that entry point.
    /// </summary>
    public void PropagateOptionsToStrategies()
    {
        foreach (var strategy in DetectionStrategies)
        {
            switch (strategy)
            {
                case HeaderTimeZoneStrategy header:
                    header.HeaderName = TimezoneHeader;
                    break;
                case ClaimsTimeZoneStrategy claims:
                    claims.ClaimType = TimezoneClaim;
                    break;
                case QueryStringTimeZoneStrategy queryString:
                    queryString.QueryParameter = TimezoneQueryParameter;
                    break;
                case CookieTimeZoneStrategy cookie:
                    cookie.CookieName = TimezoneCookie;
                    break;
            }
        }
    }
}
