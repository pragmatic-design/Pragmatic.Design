using System.Globalization;

namespace Pragmatic.Temporal.Types;

public readonly partial struct LocalDate
{
    #region Parsing & Formatting

    /// <summary>Returns the date in ISO 8601 format (yyyy-MM-dd).</summary>
    public override string ToString()
    {
        return _value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats the date using the specified format string.</summary>
    public string ToString(string format)
    {
        return _value.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>Formats the date using the specified format and culture.</summary>
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

    /// <summary>Parses a date string in ISO 8601 format.</summary>
    public static LocalDate Parse(string s)
    {
        if (TryParse(s, out var result))
            return result;
        throw new FormatException($"'{s}' is not a valid date format.");
    }

    /// <inheritdoc />
    static LocalDate IParsable<LocalDate>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <inheritdoc />
    static bool IParsable<LocalDate>.TryParse(string? s, IFormatProvider? provider, out LocalDate result)
        => TryParse(s, out result);

    /// <summary>Tries to parse a date string.</summary>
    public static bool TryParse(string? s, out LocalDate result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var dateOnly))
        {
            result = new LocalDate(dateOnly);
            return true;
        }

        if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateOnly))
        {
            result = new LocalDate(dateOnly);
            return true;
        }

        return false;
    }

    #endregion
}
