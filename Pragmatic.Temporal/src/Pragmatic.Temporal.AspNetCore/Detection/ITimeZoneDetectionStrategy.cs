using Microsoft.AspNetCore.Http;

namespace Pragmatic.Temporal.AspNetCore.Detection;

/// <summary>
///     Strategy for detecting client timezone from request context.
///     Strategies are evaluated in priority order until one returns a value.
/// </summary>
public interface ITimeZoneDetectionStrategy
{
    /// <summary>
    ///     Priority for ordering (lower = higher priority).
    /// </summary>
    int Priority { get; }

    /// <summary>
    ///     Attempts to detect timezone from the current context.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The detected timezone, or null to try next strategy.</returns>
    TimeZoneInfo? Detect(HttpContext context);
}