using Microsoft.AspNetCore.Http;
using Pragmatic.Temporal.Timezone;

namespace Pragmatic.Temporal.AspNetCore.Detection;

/// <summary>
///     Detects timezone from a cookie.
///     Useful for session persistence of timezone preference.
///     Default cookie name: tz
/// </summary>
public sealed class CookieTimeZoneStrategy : ITimeZoneDetectionStrategy, IRawTimeZoneDetectionStrategy
{
    /// <summary>The cookie name.</summary>
    public string CookieName { get; set; } = "tz";

    /// <inheritdoc />
    public int Priority => 300;

    /// <inheritdoc />
    public TimeZoneInfo? Detect(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(CookieName, out var value) &&
            !string.IsNullOrWhiteSpace(value) && value.Length <= 64)
            if (TimeZoneResolver.TryGetTimeZone(value, out var zone))
                return zone;
        return null;
    }

    /// <inheritdoc />
    public string? GetRawValue(HttpContext context)
    {
        return context.Request.Cookies.TryGetValue(CookieName, out var value) &&
               !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }
}