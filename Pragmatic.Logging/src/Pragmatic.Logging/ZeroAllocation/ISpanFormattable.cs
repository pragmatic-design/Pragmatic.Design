namespace Pragmatic.Logging.ZeroAllocation;

/// <summary>
/// Provides zero-allocation formatting capabilities using spans.
/// </summary>
/// <typeparam name="T">The type that supports span formatting</typeparam>
public interface ISpanFormattable<T>
{
    /// <summary>
    /// Tries to format the value into the provided span.
    /// </summary>
    /// <param name="destination">The span to write to</param>
    /// <param name="charsWritten">The number of characters written</param>
    /// <param name="format">The format string</param>
    /// <param name="provider">The format provider</param>
    /// <returns>True if formatting succeeded, false if the span was too small</returns>
    bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null);
}

/// <summary>
/// Provides span-based formatting utilities for zero-allocation logging.
/// </summary>
public static class SpanFormatter
{
    /// <summary>
    /// Tries to format an integer value into a span.
    /// </summary>
    /// <param name="value">The value to format</param>
    /// <param name="destination">The destination span</param>
    /// <param name="charsWritten">The number of characters written</param>
    /// <param name="format">The format string</param>
    /// <param name="provider">The format provider</param>
    /// <returns>True if formatting succeeded</returns>
    public static bool TryFormat(int value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        return value.TryFormat(destination, out charsWritten, format, provider);
    }

    /// <summary>
    /// Tries to format a long value into a span.
    /// </summary>
    /// <param name="value">The value to format</param>
    /// <param name="destination">The destination span</param>
    /// <param name="charsWritten">The number of characters written</param>
    /// <param name="format">The format string</param>
    /// <param name="provider">The format provider</param>
    /// <returns>True if formatting succeeded</returns>
    public static bool TryFormat(long value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        return value.TryFormat(destination, out charsWritten, format, provider);
    }

    /// <summary>
    /// Tries to format a decimal value into a span.
    /// </summary>
    /// <param name="value">The value to format</param>
    /// <param name="destination">The destination span</param>
    /// <param name="charsWritten">The number of characters written</param>
    /// <param name="format">The format string</param>
    /// <param name="provider">The format provider</param>
    /// <returns>True if formatting succeeded</returns>
    public static bool TryFormat(decimal value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        return value.TryFormat(destination, out charsWritten, format, provider);
    }

    /// <summary>
    /// Tries to format a double value into a span.
    /// </summary>
    /// <param name="value">The value to format</param>
    /// <param name="destination">The destination span</param>
    /// <param name="charsWritten">The number of characters written</param>
    /// <param name="format">The format string</param>
    /// <param name="provider">The format provider</param>
    /// <returns>True if formatting succeeded</returns>
    public static bool TryFormat(double value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        return value.TryFormat(destination, out charsWritten, format, provider);
    }

    /// <summary>
    /// Tries to format a DateTime value into a span.
    /// </summary>
    /// <param name="value">The value to format</param>
    /// <param name="destination">The destination span</param>
    /// <param name="charsWritten">The number of characters written</param>
    /// <param name="format">The format string</param>
    /// <param name="provider">The format provider</param>
    /// <returns>True if formatting succeeded</returns>
    public static bool TryFormat(DateTime value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        return value.TryFormat(destination, out charsWritten, format, provider);
    }

    /// <summary>
    /// Tries to format a Guid value into a span.
    /// </summary>
    /// <param name="value">The value to format</param>
    /// <param name="destination">The destination span</param>
    /// <param name="charsWritten">The number of characters written</param>
    /// <param name="format">The format string</param>
    /// <returns>True if formatting succeeded</returns>
    public static bool TryFormat(Guid value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default)
    {
        return value.TryFormat(destination, out charsWritten, format);
    }

    /// <summary>
    /// Tries to format a string value into a span.
    /// </summary>
    /// <param name="value">The value to format</param>
    /// <param name="destination">The destination span</param>
    /// <param name="charsWritten">The number of characters written</param>
    /// <returns>True if formatting succeeded</returns>
    public static bool TryFormat(string? value, Span<char> destination, out int charsWritten)
    {
        if (value == null)
        {
            ReadOnlySpan<char> nullText = "null";
            if (nullText.Length > destination.Length)
            {
                charsWritten = 0;
                return false;
            }

            nullText.CopyTo(destination);
            charsWritten = nullText.Length;
            return true;
        }

        ReadOnlySpan<char> valueSpan = value.AsSpan();
        if (valueSpan.Length > destination.Length)
        {
            charsWritten = 0;
            return false;
        }

        valueSpan.CopyTo(destination);
        charsWritten = valueSpan.Length;
        return true;
    }
}