using Microsoft.AspNetCore.Http;
using Pragmatic.Temporal.Timezone;

namespace Pragmatic.Temporal.AspNetCore.Detection;

/// <summary>
///     Detects timezone from an HTTP header.
///     Default header: X-Timezone
/// </summary>
public sealed class HeaderTimeZoneStrategy : ITimeZoneDetectionStrategy, IRawTimeZoneDetectionStrategy
{
    /// <summary>The header name to read timezone from.</summary>
    public string HeaderName { get; set; } = "X-Timezone";

    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public TimeZoneInfo? Detect(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var value) &&
            !string.IsNullOrWhiteSpace(value) && value.ToString().Length <= 64)
            if (TimeZoneResolver.TryGetTimeZone(value!, out var zone))
                return zone;
        return null;
    }

    /// <inheritdoc />
    public string? GetRawValue(HttpContext context)
    {
        return context.Request.Headers.TryGetValue(HeaderName, out var value) &&
               !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : null;
    }
}