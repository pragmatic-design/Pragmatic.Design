using System.Globalization;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Formatting;

/// <summary>
///     Provides globalization-aware formatting for money, numbers, and dates.
/// </summary>
/// <remarks>
///     <para>
///         This class is designed for dependency injection scenarios where you need
///         consistent formatting across a request or operation. The culture is obtained
///         from the injected <see cref="IGlobalizationContext" />.
///     </para>
///     <para>
///         For simple scenarios, use the extension methods on types directly
///         (e.g., <c>money.Format()</c>, <c>number.FormatPercent()</c>).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public class InvoiceService(GlobalizationFormatter formatter)
/// {
///     public InvoiceDto Format(Invoice invoice)
///     {
///         return new InvoiceDto
///         {
///             Total = formatter.FormatMoney(invoice.Total),
///             TaxRate = formatter.FormatPercent(invoice.TaxRate),
///             DueDate = formatter.FormatDate(invoice.DueDate),
///         };
///     }
/// }
/// </code>
/// </example>
public class GlobalizationFormatter
{
    /// <summary>
    ///     Creates a new GlobalizationFormatter using the provided context.
    /// </summary>
    /// <param name="context">The globalization context providing culture information.</param>
    public GlobalizationFormatter(IGlobalizationContext context)
    {
        Ensure.Ensure.ThrowIfNull(context);
        Culture = context.Culture;
    }

    /// <summary>
    ///     Creates a new GlobalizationFormatter for the specified culture.
    /// </summary>
    /// <param name="culture">The culture to use for formatting.</param>
    public GlobalizationFormatter(CultureInfo culture)
    {
        Ensure.Ensure.ThrowIfNull(culture);
        Culture = culture;
    }

    /// <summary>
    ///     Gets the culture used by this formatter.
    /// </summary>
    public CultureInfo Culture { get; }

    // =============================================================================
    // Money Formatting
    // =============================================================================

    /// <summary>
    ///     Formats a Money value using the formatter's culture.
    /// </summary>
    /// <param name="money">The money value to format.</param>
    /// <returns>A culture-appropriate string representation.</returns>
    public string FormatMoney(Money money)
    {
        return money.Format(Culture);
    }

    /// <summary>
    ///     Formats an amount and currency as money using the formatter's culture.
    /// </summary>
    /// <param name="amount">The monetary amount.</param>
    /// <param name="currency">The currency code.</param>
    /// <returns>A culture-appropriate string representation.</returns>
    public string FormatMoney(decimal amount, CurrencyCode currency)
    {
        return Money.From(amount, currency).Format(Culture);
    }

    // =============================================================================
    // Number Formatting
    // =============================================================================

    /// <summary>
    ///     Formats a decimal number with the specified decimal places.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="decimals">Number of decimal places (default: 2).</param>
    /// <returns>A culture-appropriate string representation.</returns>
    public string FormatNumber(decimal value, int decimals = 2)
    {
        return value.ToString($"N{decimals}", Culture);
    }

    /// <summary>
    ///     Formats an integer.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>A culture-appropriate string representation with thousand separators.</returns>
    public string FormatInteger(long value)
    {
        return value.ToString("N0", Culture);
    }

    /// <summary>
    ///     Formats a decimal as a percentage.
    /// </summary>
    /// <param name="value">The value to format (e.g., 0.1534 for 15.34%).</param>
    /// <param name="decimals">Number of decimal places (default: 2).</param>
    /// <returns>A culture-appropriate percentage string.</returns>
    public string FormatPercent(decimal value, int decimals = 2)
    {
        return value.ToString($"P{decimals}", Culture);
    }

    /// <summary>
    ///     Formats a byte count as a human-readable file size.
    /// </summary>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>A human-readable file size (e.g., "1.5 GB").</returns>
    public string FormatFileSize(long bytes)
    {
        return FormatFileSizeCore(bytes, Culture);
    }

    // =============================================================================
    // Date/Time Formatting
    // =============================================================================

    /// <summary>
    ///     Formats a date using the culture's short date pattern.
    /// </summary>
    /// <param name="date">The date to format.</param>
    /// <returns>A culture-appropriate date string.</returns>
    public string FormatDate(DateTimeOffset date)
    {
        return date.ToString("d", Culture);
    }

    /// <summary>
    ///     Formats a date using the culture's short date pattern.
    /// </summary>
    /// <param name="date">The date to format.</param>
    /// <returns>A culture-appropriate date string.</returns>
    public string FormatDate(DateOnly date)
    {
        return date.ToString("d", Culture);
    }

    /// <summary>
    ///     Formats a time using the culture's short time pattern.
    /// </summary>
    /// <param name="time">The time to format.</param>
    /// <returns>A culture-appropriate time string.</returns>
    public string FormatTime(TimeOnly time)
    {
        return time.ToString("t", Culture);
    }

    /// <summary>
    ///     Formats a time using the culture's short time pattern.
    /// </summary>
    /// <param name="dateTime">The date/time to format.</param>
    /// <returns>A culture-appropriate time string.</returns>
    public string FormatTime(DateTimeOffset dateTime)
    {
        return dateTime.ToString("t", Culture);
    }

    /// <summary>
    ///     Formats a date and time using the culture's short date and time patterns.
    /// </summary>
    /// <param name="dateTime">The date/time to format.</param>
    /// <returns>A culture-appropriate date/time string.</returns>
    public string FormatDateTime(DateTimeOffset dateTime)
    {
        return dateTime.ToString("g", Culture);
    }

    /// <summary>
    ///     Formats a date and time using the culture's long date and time patterns.
    /// </summary>
    /// <param name="dateTime">The date/time to format.</param>
    /// <returns>A culture-appropriate full date/time string.</returns>
    public string FormatDateTimeLong(DateTimeOffset dateTime)
    {
        return dateTime.ToString("F", Culture);
    }

    // =============================================================================
    // Internal Helpers
    // =============================================================================

    internal static string FormatFileSizeCore(long bytes, CultureInfo culture)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB", "PB"];

        if (bytes == 0)
            return $"0 {suffixes[0]}";

        var absBytes = Math.Abs(bytes);
        var place = Convert.ToInt32(Math.Floor(Math.Log(absBytes, 1024)));
        place = Math.Min(place, suffixes.Length - 1);

        var num = Math.Round(absBytes / Math.Pow(1024, place), 1);
        var sign = bytes < 0 ? "-" : "";

        return $"{sign}{num.ToString("N1", culture)} {suffixes[place]}";
    }
}