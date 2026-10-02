using System.Globalization;

namespace Pragmatic.Temporal.Types;

public readonly partial struct LocalDateTime
{
    #region Parsing & Formatting

    /// <summary>Returns the datetime in ISO 8601 format (yyyy-MM-ddTHH:mm:ss).</summary>
    public override string ToString()
    {
        return _value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats the datetime using the specified format string.</summary>
    public string ToString(string format)
    {
        return _value.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>Formats the datetime using the specified format and culture.</summary>
    public string ToString(string format, IFormatProvider? formatProvider)
    {
        return _value.ToString(format, formatProvider);
    }

    /// <inheritdoc />
    string IFormattable.ToString(string? format, IFormatProvider? formatProvider)
    {
        return string.IsNullOrEmpty(format)
            ? ToString()
            : _value.ToString(format, formatProvider);
    }

    /// <summary>Parses a datetime string in ISO 8601 format.</summary>
    public static LocalDateTime Parse(string s)
    {
        if (TryParse(s, out var result))
            return result;
        throw new FormatException($"'{s}' is not a valid datetime format.");
    }

    /// <inheritdoc />
    static LocalDateTime IParsable<LocalDateTime>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <inheritdoc />
    static bool IParsable<LocalDateTime>.TryParse(string? s, IFormatProvider? provider, out LocalDateTime result)
        => TryParse(s, out result);

    /// <summary>Tries to parse a datetime string.</summary>
    public static bool TryParse(string? s, out LocalDateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        // Remove timezone suffix if present (we ignore it for LocalDateTime)
        var span = s.AsSpan();
        var tzIndex = span.LastIndexOfAny(['Z', '+', '-']);
        if (tzIndex > 10) // Only if after the date part
        {
            span = span[..tzIndex];
            s = span.ToString();
        }

        if (DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dateTime) ||
            DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out dateTime) ||
            DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out dateTime) ||
            DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out dateTime))
        {
            result = new LocalDateTime(dateTime);
            return true;
        }

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
        {
            result = new LocalDateTime(dateTime);
            return true;
        }

        return false;
    }

    #endregion
}
