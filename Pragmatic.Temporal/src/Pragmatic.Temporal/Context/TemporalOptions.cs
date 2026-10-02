using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Context;

/// <summary>
///     Configuration options for the temporal pipeline.
/// </summary>
public sealed class TemporalOptions
{
    /// <summary>
    ///     Default timezone for the server/application.
    ///     Used when no client timezone is available.
    ///     Default: UTC
    /// </summary>
    public TimeZoneInfo DefaultTimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    ///     Business timezone for calculations like "end of day", "business hours".
    ///     Example: Company HQ is in New York, so business logic uses America/New_York.
    ///     Default: UTC
    /// </summary>
    public TimeZoneInfo BusinessTimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    ///     Default country code for holiday calculations.
    ///     Example: "IT" for Italy.
    ///     Default: null (no holidays by default)
    /// </summary>
    public string? DefaultCountryCode { get; set; }

    /// <summary>
    ///     How to handle ambiguous times (during DST fall-back).
    ///     Default: UseStandardTime
    /// </summary>
    public AmbiguousTimePolicy AmbiguousTimeHandling { get; set; } = AmbiguousTimePolicy.UseStandardTime;

    /// <summary>
    ///     How to handle non-existent times (during DST spring-forward).
    ///     Default: ShiftForward
    /// </summary>
    public NonExistentTimePolicy NonExistentTimeHandling { get; set; } = NonExistentTimePolicy.ShiftForward;

    /// <summary>
    ///     Whether to throw when an invalid timezone ID is provided.
    ///     If false, logs warning and uses DefaultTimeZone.
    ///     Default: false (resilient)
    /// </summary>
    public bool ThrowOnInvalidTimeZone { get; set; }

    /// <summary>
    ///     First day of the week for week calculations.
    ///     Default: Monday (ISO 8601 standard)
    /// </summary>
    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;
}