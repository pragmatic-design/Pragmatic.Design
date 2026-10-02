using System.Globalization;

namespace Pragmatic.Temporal.Types;

public readonly partial struct LocalTime
{
    #region Parsing & Formatting

    /// <summary>Returns the time in ISO 8601 format (HH:mm:ss).</summary>
    public override string ToString()
    {
        return _value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats the time using the specified format string.</summary>
    public string ToString(string format)
    {
        return _value.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>Formats the time using the specified format and culture.</summary>
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

    /// <summary>Parses a time string in ISO 8601 format.</summary>
    public static LocalTime Parse(string s)
    {
        if (TryParse(s, out var result))
            return result;
        throw new FormatException($"'{s}' is not a valid time format.");
    }

    /// <inheritdoc />
    static LocalTime IParsable<LocalTime>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <inheritdoc />
    static bool IParsable<LocalTime>.TryParse(string? s, IFormatProvider? provider, out LocalTime result)
        => TryParse(s, out result);

    /// <summary>Tries to parse a time string.</summary>
    public static bool TryParse(string? s, out LocalTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        if (TimeOnly.TryParseExact(s, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var timeOnly) ||
            TimeOnly.TryParseExact(s, "HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out timeOnly) ||
            TimeOnly.TryParseExact(s, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out timeOnly))
        {
            result = new LocalTime(timeOnly);
            return true;
        }

        if (TimeOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out timeOnly))
        {
            result = new LocalTime(timeOnly);
            return true;
        }

        return false;
    }

    #endregion
}
