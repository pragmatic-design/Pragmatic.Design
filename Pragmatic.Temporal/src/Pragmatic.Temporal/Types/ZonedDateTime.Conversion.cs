using System.Globalization;
using Pragmatic.Temporal.Timezone;

namespace Pragmatic.Temporal.Types;

/// <summary>
///     Conversion, arithmetic, and formatting methods for <see cref="ZonedDateTime" />.
/// </summary>
public readonly partial struct ZonedDateTime
{
    #region Conversion Methods

    /// <summary>Converts to a different timezone (same instant, different local time).</summary>
    public ZonedDateTime InZone(TimeZoneInfo zone)
    {
        return FromUtc(_utcInstant, zone);
    }

    /// <summary>Converts to a different timezone (same instant, different local time).</summary>
    public ZonedDateTime InZone(string timezoneId)
    {
        return FromUtc(_utcInstant, timezoneId);
    }

    /// <summary>Converts to UTC DateTimeOffset.</summary>
    public DateTimeOffset ToUtc()
    {
        return _utcInstant;
    }

    /// <summary>
    ///     Converts to DateTimeOffset with the zone's current offset.
    ///     Useful for APIs that expect DateTimeOffset.
    /// </summary>
    public DateTimeOffset ToDateTimeOffset()
    {
        return new DateTimeOffset(LocalDateTime, Offset);
    }

    #endregion

    #region Arithmetic (Calendar-based, DST-safe)

    /// <summary>
    ///     Adds calendar days (NOT 24-hour periods).
    ///     Result maintains same local time-of-day if possible.
    /// </summary>
    public ZonedDateTime AddDays(int days)
    {
        var newLocal = LocalDateTime.AddDays(days);
        return FromLocal(newLocal, Zone);
    }

    /// <summary>Adds calendar months, maintaining the same day if possible.</summary>
    public ZonedDateTime AddMonths(int months)
    {
        var newLocal = LocalDateTime.AddMonths(months);
        return FromLocal(newLocal, Zone);
    }

    /// <summary>Adds calendar years, maintaining the same day if possible.</summary>
    public ZonedDateTime AddYears(int years)
    {
        var newLocal = LocalDateTime.AddYears(years);
        return FromLocal(newLocal, Zone);
    }

    /// <summary>Adds hours (wall clock hours, may skip/repeat during DST).</summary>
    public ZonedDateTime AddHours(int hours)
    {
        var newLocal = LocalDateTime.AddHours(hours);
        return FromLocal(newLocal, Zone);
    }

    /// <summary>Adds minutes.</summary>
    public ZonedDateTime AddMinutes(int minutes)
    {
        var newLocal = LocalDateTime.AddMinutes(minutes);
        return FromLocal(newLocal, Zone);
    }

    /// <summary>
    ///     Adds a physical duration (exact elapsed time).
    ///     May result in different local time-of-day during DST transitions.
    /// </summary>
    public ZonedDateTime Add(Duration duration)
    {
        return FromUtc(_utcInstant.Add(duration.ToTimeSpan()), Zone);
    }

    /// <summary>
    ///     Adds a physical duration (exact elapsed time).
    /// </summary>
    public ZonedDateTime Add(TimeSpan timeSpan)
    {
        return FromUtc(_utcInstant.Add(timeSpan), Zone);
    }

    /// <summary>
    ///     Gets the physical duration between this instant and another.
    /// </summary>
    public Duration DurationUntil(ZonedDateTime other)
    {
        return Duration.FromTimeSpan(other._utcInstant - _utcInstant);
    }

    #endregion

    #region Formatting & Parsing

    /// <summary>
    ///     Returns the datetime in ISO 8601 format with timezone annotation.
    ///     Example: "2024-01-15T10:30:00+01:00[Europe/Rome]"
    /// </summary>
    public override string ToString()
    {
        var dto = ToDateTimeOffset();
        return $"{dto:yyyy-MM-dd'T'HH:mm:sszzz}[{ZoneId}]";
    }

    /// <summary>Formats the datetime using the specified format string.</summary>
    public string ToString(string format)
    {
        var dto = ToDateTimeOffset();
        return dto.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Parses a ZonedDateTime string.
    ///     Supports formats:
    ///     - "2024-01-15T10:30:00+01:00[Europe/Rome]"
    ///     - "2024-01-15T10:30:00Z" (UTC)
    /// </summary>
    public static ZonedDateTime Parse(string s)
    {
        if (TryParse(s, out var result))
            return result;
        throw new FormatException($"'{s}' is not a valid ZonedDateTime format.");
    }

    /// <summary>Tries to parse a ZonedDateTime string.</summary>
    public static bool TryParse(string? s, out ZonedDateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        // Check for timezone annotation: "2024-01-15T10:30:00+01:00[Europe/Rome]"
        var bracketStart = s.IndexOf('[');
        if (bracketStart > 0 && s.EndsWith("]", StringComparison.Ordinal))
        {
            var timezoneId = s[(bracketStart + 1)..^1];
            var dateTimePart = s[..bracketStart];

            // Guard against excessively long or clearly invalid timezone IDs before resolver lookup
            if (timezoneId.Length > 64)
                return false;

            if (!DateTimeOffset.TryParse(dateTimePart, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dto))
                return false;

            if (!TimeZoneResolver.TryGetTimeZone(timezoneId, out var zone) || zone is null)
                return false;

            // When the input carries BOTH an explicit offset and a [Zone] annotation,
            // the offset must be a valid zone offset for the parsed local wall time.
            // Otherwise the value is internally inconsistent and is silently reinterpreted.
            if (HasExplicitOffset(dateTimePart) && !OffsetIsValidForZone(dto, zone))
                return false;

            result = FromUtc(dto.ToUniversalTime(), zone);
            return true;
        }

        // Try parsing as DateTimeOffset (defaults to UTC)
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsedDto))
        {
            result = FromUtc(parsedDto.ToUniversalTime(), TimeZoneInfo.Utc);
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Detects whether the date-time portion of the input carries an explicit offset
    ///     (a trailing "Z" or a "+hh:mm" / "-hh:mm" suffix) rather than a bare local time.
    /// </summary>
    private static bool HasExplicitOffset(string dateTimePart)
    {
        if (dateTimePart.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            return true;

        // A signed offset can only appear after the time component; the date itself
        // uses '-' as a separator, so look past the 'T' (or space) date/time delimiter.
        var timeStart = dateTimePart.IndexOfAny(['T', 't', ' ']);
        if (timeStart < 0)
            return false;

        var timePart = dateTimePart[(timeStart + 1)..];
        return timePart.Contains('+') || timePart.Contains('-');
    }

    /// <summary>
    ///     Returns true if the explicit offset of <paramref name="dto" /> is a valid UTC
    ///     offset for <paramref name="zone" /> at the parsed local wall-clock time.
    /// </summary>
    private static bool OffsetIsValidForZone(DateTimeOffset dto, TimeZoneInfo zone)
    {
        var wallTime = dto.DateTime;

        // A wall time inside a DST gap is not a real local time in this zone.
        if (zone.IsInvalidTime(wallTime))
            return false;

        if (zone.IsAmbiguousTime(wallTime))
        {
            foreach (var candidate in zone.GetAmbiguousTimeOffsets(wallTime))
                if (candidate == dto.Offset)
                    return true;
            return false;
        }

        return zone.GetUtcOffset(wallTime) == dto.Offset;
    }

    #endregion
}
