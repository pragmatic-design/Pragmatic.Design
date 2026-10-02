using Pragmatic.Internationalization.Context;

namespace Pragmatic.Internationalization.Extensions;

/// <summary>
///     Extension methods for date and time formatting with globalization support.
/// </summary>
public static class DateExtensions
{
    // =============================================================================
    // DateTimeOffset Extensions
    // =============================================================================

    /// <param name="date">The date to format.</param>
    extension(DateTimeOffset date)
    {
        /// <summary>
        ///     Formats the date using the culture's short date pattern.
        /// </summary>
        /// <returns>A culture-appropriate date string (e.g., "01/15/2024" or "15.01.2024").</returns>
        public string FormatDate()
        {
            return date.ToString("d", I18NContext.EffectiveCulture);
        }

        /// <summary>
        ///     Formats the time using the culture's short time pattern.
        /// </summary>
        /// <returns>A culture-appropriate time string (e.g., "2:30 PM" or "14:30").</returns>
        public string FormatTime()
        {
            return date.ToString("t", I18NContext.EffectiveCulture);
        }

        /// <summary>
        ///     Formats the date and time using the culture's short patterns.
        /// </summary>
        /// <returns>A culture-appropriate date/time string.</returns>
        public string FormatDateTime()
        {
            return date.ToString("g", I18NContext.EffectiveCulture);
        }

        /// <summary>
        ///     Formats the date and time using the culture's long patterns.
        /// </summary>
        /// <returns>A culture-appropriate full date/time string.</returns>
        public string FormatDateTimeLong()
        {
            return date.ToString("F", I18NContext.EffectiveCulture);
        }
    }

    // =============================================================================
    // DateTime Extensions
    // =============================================================================

    /// <param name="date">The date to format.</param>
    extension(DateTime date)
    {
        /// <summary>
        ///     Formats the date using the culture's short date pattern.
        /// </summary>
        /// <returns>A culture-appropriate date string.</returns>
        public string FormatDate()
        {
            return date.ToString("d", I18NContext.EffectiveCulture);
        }

        /// <summary>
        ///     Formats the time using the culture's short time pattern.
        /// </summary>
        /// <returns>A culture-appropriate time string.</returns>
        public string FormatTime()
        {
            return date.ToString("t", I18NContext.EffectiveCulture);
        }

        /// <summary>
        ///     Formats the date and time using the culture's short patterns.
        /// </summary>
        /// <returns>A culture-appropriate date/time string.</returns>
        public string FormatDateTime()
        {
            return date.ToString("g", I18NContext.EffectiveCulture);
        }
    }

    // =============================================================================
    // DateOnly Extensions
    // =============================================================================

    /// <param name="date">The date to format.</param>
    extension(DateOnly date)
    {
        /// <summary>
        ///     Formats the date using the culture's short date pattern.
        /// </summary>
        /// <returns>A culture-appropriate date string.</returns>
        public string FormatDate()
        {
            return date.ToString("d", I18NContext.EffectiveCulture);
        }

        /// <summary>
        ///     Formats the date using the culture's long date pattern.
        /// </summary>
        /// <returns>A culture-appropriate long date string.</returns>
        public string FormatDateLong()
        {
            return date.ToString("D", I18NContext.EffectiveCulture);
        }
    }

    // =============================================================================
    // TimeOnly Extensions
    // =============================================================================

    /// <param name="time">The time to format.</param>
    extension(TimeOnly time)
    {
        /// <summary>
        ///     Formats the time using the culture's short time pattern.
        /// </summary>
        /// <returns>A culture-appropriate time string.</returns>
        public string FormatTime()
        {
            return time.ToString("t", I18NContext.EffectiveCulture);
        }

        /// <summary>
        ///     Formats the time using the culture's long time pattern.
        /// </summary>
        /// <returns>A culture-appropriate long time string.</returns>
        public string FormatTimeLong()
        {
            return time.ToString("T", I18NContext.EffectiveCulture);
        }
    }
}