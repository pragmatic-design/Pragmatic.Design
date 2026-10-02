using Microsoft.AspNetCore.Http;

namespace Pragmatic.Temporal.AspNetCore.Detection;

/// <summary>
///     Optional extension of <see cref="ITimeZoneDetectionStrategy" /> that exposes the raw
///     timezone value a client supplied, independent of whether it resolved to a valid zone.
///     Lets the middleware distinguish "no timezone supplied" from "supplied but invalid",
///     which is required to honor <c>TemporalOptions.ThrowOnInvalidTimeZone</c>.
/// </summary>
public interface IRawTimeZoneDetectionStrategy
{
    /// <summary>
    ///     Returns the raw timezone value supplied by the client for this strategy, or
    ///     <c>null</c> if the client supplied nothing for this strategy's source.
    /// </summary>
    string? GetRawValue(HttpContext context);
}
