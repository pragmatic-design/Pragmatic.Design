using Microsoft.AspNetCore.Http;
using Pragmatic.Temporal.Timezone;

namespace Pragmatic.Temporal.AspNetCore.Detection;

/// <summary>
///     Detects timezone from user claims (JWT/Identity).
///     Default claim type: timezone
/// </summary>
public sealed class ClaimsTimeZoneStrategy : ITimeZoneDetectionStrategy, IRawTimeZoneDetectionStrategy
{
    /// <summary>The claim type to read timezone from.</summary>
    public string ClaimType { get; set; } = "timezone";

    /// <inheritdoc />
    public int Priority => 200;

    /// <inheritdoc />
    public TimeZoneInfo? Detect(HttpContext context)
    {
        var claim = context.User.FindFirst(ClaimType);
        if (claim != null && !string.IsNullOrWhiteSpace(claim.Value) && claim.Value.Length <= 64)
            if (TimeZoneResolver.TryGetTimeZone(claim.Value, out var zone))
                return zone;
        return null;
    }

    /// <inheritdoc />
    public string? GetRawValue(HttpContext context)
    {
        var claim = context.User.FindFirst(ClaimType);
        return claim != null && !string.IsNullOrWhiteSpace(claim.Value)
            ? claim.Value
            : null;
    }
}