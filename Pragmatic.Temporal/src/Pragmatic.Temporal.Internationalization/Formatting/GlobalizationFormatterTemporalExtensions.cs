using Pragmatic.Internationalization.Formatting;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Internationalization.Formatting;

/// <summary>
///     Temporal-type overloads for <see cref="GlobalizationFormatter" />, mirroring its
///     BCL overloads. <see cref="LocalDateTime" /> formats its wall time as-is (no timezone
///     conversion); <see cref="ZonedDateTime" /> formats the wall time in its own zone.
/// </summary>
/// <remarks>
///     <see cref="LocalDate" /> and <see cref="LocalTime" /> need no overloads: they convert
///     implicitly to <see cref="DateOnly" /> / <see cref="TimeOnly" />, so the existing
///     formatter methods already accept them.
/// </remarks>
public static class GlobalizationFormatterTemporalExtensions
{
    /// <param name="formatter">The formatter providing the culture.</param>
    extension(GlobalizationFormatter formatter)
    {
        /// <summary>
        ///     Formats the date component of a wall-clock date/time using the culture's short date pattern.
        /// </summary>
        /// <param name="dateTime">The wall-clock date/time to format.</param>
        /// <returns>A culture-appropriate date string.</returns>
        public string FormatDate(LocalDateTime dateTime)
        {
            return formatter.FormatDate((DateOnly)dateTime.Date);
        }

        /// <summary>
        ///     Formats the time component of a wall-clock date/time using the culture's short time pattern.
        /// </summary>
        /// <param name="dateTime">The wall-clock date/time to format.</param>
        /// <returns>A culture-appropriate time string.</returns>
        public string FormatTime(LocalDateTime dateTime)
        {
            return formatter.FormatTime((TimeOnly)dateTime.Time);
        }

        /// <summary>
        ///     Formats a wall-clock date/time using the culture's short date and time patterns.
        /// </summary>
        /// <param name="dateTime">The wall-clock date/time to format.</param>
        /// <returns>A culture-appropriate date/time string.</returns>
        public string FormatDateTime(LocalDateTime dateTime)
        {
            // The "g"/"F" patterns never render an offset, so a zero-offset wrapper delegates
            // to the existing DateTimeOffset overload without altering the wall components.
            return formatter.FormatDateTime(new DateTimeOffset(dateTime.ToDateTime(), TimeSpan.Zero));
        }

        /// <summary>
        ///     Formats a wall-clock date/time using the culture's long date and time patterns.
        /// </summary>
        /// <param name="dateTime">The wall-clock date/time to format.</param>
        /// <returns>A culture-appropriate full date/time string.</returns>
        public string FormatDateTimeLong(LocalDateTime dateTime)
        {
            return formatter.FormatDateTimeLong(new DateTimeOffset(dateTime.ToDateTime(), TimeSpan.Zero));
        }

        /// <summary>
        ///     Formats the date of a zoned date/time — as seen in its own zone — using the culture's short date pattern.
        /// </summary>
        /// <param name="dateTime">The zoned date/time to format.</param>
        /// <returns>A culture-appropriate date string for the zone's wall date.</returns>
        public string FormatDate(ZonedDateTime dateTime)
        {
            return formatter.FormatDate(dateTime.ToDateTimeOffset());
        }

        /// <summary>
        ///     Formats the time of a zoned date/time — as seen in its own zone — using the culture's short time pattern.
        /// </summary>
        /// <param name="dateTime">The zoned date/time to format.</param>
        /// <returns>A culture-appropriate time string for the zone's wall time.</returns>
        public string FormatTime(ZonedDateTime dateTime)
        {
            return formatter.FormatTime(dateTime.ToDateTimeOffset());
        }

        /// <summary>
        ///     Formats a zoned date/time — as seen in its own zone — using the culture's short date and time patterns.
        /// </summary>
        /// <param name="dateTime">The zoned date/time to format.</param>
        /// <returns>A culture-appropriate date/time string for the zone's wall time.</returns>
        public string FormatDateTime(ZonedDateTime dateTime)
        {
            return formatter.FormatDateTime(dateTime.ToDateTimeOffset());
        }

        /// <summary>
        ///     Formats a zoned date/time — as seen in its own zone — using the culture's long date and time patterns.
        /// </summary>
        /// <param name="dateTime">The zoned date/time to format.</param>
        /// <returns>A culture-appropriate full date/time string for the zone's wall time.</returns>
        public string FormatDateTimeLong(ZonedDateTime dateTime)
        {
            return formatter.FormatDateTimeLong(dateTime.ToDateTimeOffset());
        }
    }
}
