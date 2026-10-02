using Microsoft.AspNetCore.Http;
using Pragmatic.Temporal.Timezone;

namespace Pragmatic.Temporal.AspNetCore.Detection;

/// <summary>
///     Detects timezone from query string parameter.
///     Useful for debugging or one-off requests.
///     Default parameter: tz
/// </summary>
public sealed class QueryStringTimeZoneStrategy : ITimeZoneDetectionStrategy, IRawTimeZoneDetectionStrategy
{
    /// <summary>The query parameter name.</summary>
    public string QueryParameter { get; set; } = "tz";

    /// <inheritdoc />
    public int Priority => 50; // High priority for debugging

    /// <inheritdoc />
    public TimeZoneInfo? Detect(HttpContext context)
    {
        if (context.Request.Query.TryGetValue(QueryParameter, out var value) &&
            !string.IsNullOrWhiteSpace(value) && value.ToString().Length <= 64)
            if (TimeZoneResolver.TryGetTimeZone(value!, out var zone))
                return zone;
        return null;
    }

    /// <inheritdoc />
    public string? GetRawValue(HttpContext context)
    {
        return context.Request.Query.TryGetValue(QueryParameter, out var value) &&
               !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : null;
    }
}