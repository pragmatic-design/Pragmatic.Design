using Pragmatic.Temporal.Timezone;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Extensions;

/// <summary>
///     Extension methods on <see cref="DateTimeOffset" /> for timezone-aware conversion
///     to Pragmatic.Temporal types.
/// </summary>
public static class DateTimeOffsetExtensions
{
    /// <param name="dto">The DateTimeOffset to convert.</param>
    extension(DateTimeOffset dto)
    {
        /// <summary>
        ///     Converts to a <see cref="LocalDate" /> in the specified timezone.
        /// </summary>
        /// <param name="zone">The timezone to interpret the date in.</param>
        /// <returns>The local date in the specified timezone.</returns>
        public LocalDate ToLocalDate(TimeZoneInfo zone)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(dto.UtcDateTime, zone);
            return new LocalDate(DateOnly.FromDateTime(local));
        }

        /// <summary>
        ///     Converts to a <see cref="LocalDate" /> in the specified timezone.
        /// </summary>
        /// <param name="timezoneId">The IANA or Windows timezone ID.</param>
        /// <returns>The local date in the specified timezone.</returns>
        public LocalDate ToLocalDate(string timezoneId)
        {
            return dto.ToLocalDate(TimeZoneResolver.GetTimeZone(timezoneId));
        }

        /// <summary>
        ///     Converts to a <see cref="LocalTime" /> in the specified timezone.
        /// </summary>
        /// <param name="zone">The timezone to interpret the time in.</param>
        /// <returns>The local time in the specified timezone.</returns>
        public LocalTime ToLocalTime(TimeZoneInfo zone)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(dto.UtcDateTime, zone);
            return new LocalTime(TimeOnly.FromDateTime(local));
        }

        /// <summary>
        ///     Converts to a <see cref="LocalTime" /> in the specified timezone.
        /// </summary>
        /// <param name="timezoneId">The IANA or Windows timezone ID.</param>
        /// <returns>The local time in the specified timezone.</returns>
        public LocalTime ToLocalTime(string timezoneId)
        {
            return dto.ToLocalTime(TimeZoneResolver.GetTimeZone(timezoneId));
        }

        /// <summary>
        ///     Converts to a <see cref="LocalDateTime" /> in the specified timezone.
        /// </summary>
        /// <param name="zone">The timezone to interpret the datetime in.</param>
        /// <returns>The local datetime in the specified timezone.</returns>
        public LocalDateTime ToLocalDateTime(TimeZoneInfo zone)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(dto.UtcDateTime, zone);
            return new LocalDateTime(local);
        }

        /// <summary>
        ///     Converts to a <see cref="LocalDateTime" /> in the specified timezone.
        /// </summary>
        /// <param name="timezoneId">The IANA or Windows timezone ID.</param>
        /// <returns>The local datetime in the specified timezone.</returns>
        public LocalDateTime ToLocalDateTime(string timezoneId)
        {
            return dto.ToLocalDateTime(TimeZoneResolver.GetTimeZone(timezoneId));
        }
    }
}
