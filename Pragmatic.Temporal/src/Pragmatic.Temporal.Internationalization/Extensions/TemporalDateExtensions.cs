using Pragmatic.Internationalization.Extensions;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Internationalization.Extensions;

/// <summary>
///     Ambient-culture formatting extensions for <see cref="LocalDateTime" /> and
///     <see cref="ZonedDateTime" />, mirroring Pragmatic.Internationalization's
///     <c>DateExtensions</c>. The culture comes from the current I18N context
///     (falling back to <see cref="System.Globalization.CultureInfo.CurrentCulture" />).
/// </summary>
/// <remarks>
///     <see cref="LocalDate" /> and <see cref="LocalTime" /> need no bridge: they convert
///     implicitly to <see cref="DateOnly" /> / <see cref="TimeOnly" />, whose extensions
///     already exist. All members delegate to the existing BCL extensions — no formatting
///     logic lives here.
/// </remarks>
public static class TemporalDateExtensions
{
    /// <param name="dateTime">The wall-clock date/time to format.</param>
    extension(LocalDateTime dateTime)
    {
        /// <summary>
        ///     Formats the date using the culture's short date pattern.
        /// </summary>
        /// <returns>A culture-appropriate date string.</returns>
        public string FormatDate()
        {
            return dateTime.ToDateTime().FormatDate();
        }

        /// <summary>
        ///     Formats the time using the culture's short time pattern.
        /// </summary>
        /// <returns>A culture-appropriate time string.</returns>
        public string FormatTime()
        {
            return dateTime.ToDateTime().FormatTime();
        }

        /// <summary>
        ///     Formats the date and time using the culture's short patterns.
        /// </summary>
        /// <returns>A culture-appropriate date/time string.</returns>
        public string FormatDateTime()
        {
            return dateTime.ToDateTime().FormatDateTime();
        }

        /// <summary>
        ///     Formats the date and time using the culture's long patterns.
        /// </summary>
        /// <returns>A culture-appropriate full date/time string.</returns>
        public string FormatDateTimeLong()
        {
            // The "F" pattern never renders an offset, so a zero-offset wrapper delegates
            // to the DateTimeOffset extension without altering the wall components.
            return new DateTimeOffset(dateTime.ToDateTime(), TimeSpan.Zero).FormatDateTimeLong();
        }
    }

    /// <param name="dateTime">The zoned date/time to format (wall time in its own zone).</param>
    extension(ZonedDateTime dateTime)
    {
        /// <summary>
        ///     Formats the date — as seen in the value's own zone — using the culture's short date pattern.
        /// </summary>
        /// <returns>A culture-appropriate date string for the zone's wall date.</returns>
        public string FormatDate()
        {
            return dateTime.ToDateTimeOffset().FormatDate();
        }

        /// <summary>
        ///     Formats the time — as seen in the value's own zone — using the culture's short time pattern.
        /// </summary>
        /// <returns>A culture-appropriate time string for the zone's wall time.</returns>
        public string FormatTime()
        {
            return dateTime.ToDateTimeOffset().FormatTime();
        }

        /// <summary>
        ///     Formats the date and time — as seen in the value's own zone — using the culture's short patterns.
        /// </summary>
        /// <returns>A culture-appropriate date/time string for the zone's wall time.</returns>
        public string FormatDateTime()
        {
            return dateTime.ToDateTimeOffset().FormatDateTime();
        }

        /// <summary>
        ///     Formats the date and time — as seen in the value's own zone — using the culture's long patterns.
        /// </summary>
        /// <returns>A culture-appropriate full date/time string for the zone's wall time.</returns>
        public string FormatDateTimeLong()
        {
            return dateTime.ToDateTimeOffset().FormatDateTimeLong();
        }
    }
}
