using System.Text;
using System.Text.RegularExpressions;

namespace Pragmatic.Temporal.Types;

public readonly partial struct Period
{
    #region Formatting & Parsing

    private static readonly Regex IsoPattern = new(
        @"^P(?:(-?\d+)Y)?(?:(-?\d+)M)?(?:(-?\d+)D)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    ///     Returns the period in ISO 8601 duration format: P1Y2M3D.
    /// </summary>
    public override string ToString()
    {
        if (IsZero)
            return "P0D";

        var sb = new StringBuilder("P");

        if (Years != 0)
            sb.Append($"{Years}Y");
        if (Months != 0)
            sb.Append($"{Months}M");
        if (Days != 0)
            sb.Append($"{Days}D");

        // If only zeroes were skipped, add 0D
        if (sb.Length == 1)
            sb.Append("0D");

        return sb.ToString();
    }

    /// <summary>
    ///     Returns a human-readable representation.
    /// </summary>
    public string ToDisplayString()
    {
        if (IsZero)
            return "0 days";

        var parts = new List<string>();

        if (Years != 0)
            parts.Add(Years == 1 || Years == -1 ? $"{Years} year" : $"{Years} years");
        if (Months != 0)
            parts.Add(Months == 1 || Months == -1 ? $"{Months} month" : $"{Months} months");
        if (Days != 0)
            parts.Add(Days == 1 || Days == -1 ? $"{Days} day" : $"{Days} days");

        return string.Join(", ", parts);
    }

    /// <inheritdoc />
    string IFormattable.ToString(string? format, IFormatProvider? formatProvider)
    {
        // Period uses ISO 8601 format; custom format strings are not supported
        return ToString();
    }

    /// <summary>
    ///     Parses an ISO 8601 duration string (P1Y2M3D).
    /// </summary>
    public static Period Parse(string s)
    {
        if (TryParse(s, out var result))
            return result;
        throw new FormatException($"'{s}' is not a valid ISO 8601 period format. Expected: P1Y2M3D.");
    }

    /// <inheritdoc />
    static Period IParsable<Period>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <inheritdoc />
    static bool IParsable<Period>.TryParse(string? s, IFormatProvider? provider, out Period result)
        => TryParse(s, out result);

    /// <summary>
    ///     Tries to parse an ISO 8601 duration string.
    /// </summary>
    public static bool TryParse(string? s, out Period result)
    {
        result = Zero;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var match = IsoPattern.Match(s.Trim());
        if (!match.Success)
            return false;

        // At least one component must be present
        if (!match.Groups[1].Success && !match.Groups[2].Success && !match.Groups[3].Success)
            return false;

        var years = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        var months = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        var days = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

        result = new Period(years, months, days);
        return true;
    }

    #endregion
}
