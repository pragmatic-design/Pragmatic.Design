using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Formatting;

namespace Pragmatic.Internationalization.Extensions;

/// <summary>
///     Extension methods for number formatting with globalization support.
/// </summary>
public static class NumberExtensions
{
    // =============================================================================
    // Decimal Extensions
    // =============================================================================

    /// <param name="value">The value to format.</param>
    extension(decimal value)
    {
        /// <summary>
        ///     Formats a decimal number using the current culture.
        /// </summary>
        /// <param name="decimals">Number of decimal places (default: 2).</param>
        /// <returns>A culture-appropriate string representation.</returns>
        public string FormatNumber(int decimals = 2)
        {
            return value.ToString($"N{decimals}", I18NContext.EffectiveCulture);
        }

        /// <summary>
        ///     Formats a decimal as a percentage using the current culture.
        /// </summary>
        /// <param name="decimals">Number of decimal places (default: 2).</param>
        /// <returns>A culture-appropriate percentage string.</returns>
        public string FormatPercent(int decimals = 2)
        {
            return value.ToString($"P{decimals}", I18NContext.EffectiveCulture);
        }
    }

    // =============================================================================
    // Double Extensions
    // =============================================================================

    /// <param name="value">The value to format.</param>
    extension(double value)
    {
        /// <summary>
        ///     Formats a double number using the current culture.
        /// </summary>
        /// <param name="decimals">Number of decimal places (default: 2).</param>
        /// <returns>A culture-appropriate string representation.</returns>
        public string FormatNumber(int decimals = 2)
        {
            return value.ToString($"N{decimals}", I18NContext.EffectiveCulture);
        }

        /// <summary>
        ///     Formats a double as a percentage using the current culture.
        /// </summary>
        /// <param name="decimals">Number of decimal places (default: 2).</param>
        /// <returns>A culture-appropriate percentage string.</returns>
        public string FormatPercent(int decimals = 2)
        {
            return value.ToString($"P{decimals}", I18NContext.EffectiveCulture);
        }
    }

    // =============================================================================
    // Integer Extensions
    // =============================================================================

    /// <summary>
    ///     Formats an integer with thousand separators using the current culture.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>A culture-appropriate string representation.</returns>
    public static string FormatNumber(this int value)
    {
        return value.ToString("N0", I18NContext.EffectiveCulture);
    }

    /// <param name="value">The value to format.</param>
    extension(long value)
    {
        /// <summary>
        ///     Formats a long integer with thousand separators using the current culture.
        /// </summary>
        /// <returns>A culture-appropriate string representation.</returns>
        public string FormatNumber()
        {
            return value.ToString("N0", I18NContext.EffectiveCulture);
        }

        /// <summary>
        ///     Formats a byte count as a human-readable file size.
        /// </summary>
        /// <returns>A human-readable file size (e.g., "1.5 GB").</returns>
        public string FormatFileSize()
        {
            return GlobalizationFormatter.FormatFileSizeCore(value, I18NContext.EffectiveCulture);
        }
    }

    // =============================================================================
    // File Size Extensions
    // =============================================================================

    /// <summary>
    ///     Formats a byte count as a human-readable file size.
    /// </summary>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>A human-readable file size (e.g., "1.5 GB").</returns>
    public static string FormatFileSize(this int bytes)
    {
        return GlobalizationFormatter.FormatFileSizeCore(bytes, I18NContext.EffectiveCulture);
    }
}