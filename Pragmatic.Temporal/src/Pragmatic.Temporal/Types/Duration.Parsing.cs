using System.Globalization;

namespace Pragmatic.Temporal.Types;

public readonly partial struct Duration
{
    #region Formatting & Parsing

    /// <inheritdoc />
    string IFormattable.ToString(string? format, IFormatProvider? formatProvider)
    {
        // Duration uses ISO 8601 format; custom format strings are not supported
        return ToString();
    }

    /// <summary>Returns an ISO 8601 duration string (e.g., "PT1H30M").</summary>
    public override string ToString()
    {
        if (IsZero)
            return "PT0S";

        var abs = _value.Duration();
        var negative = IsNegative ? "-" : "";

        var parts = new List<string>();

        if (abs.Days > 0)
            parts.Add($"{abs.Days}D");

        var timeParts = new List<string>();
        if (abs.Hours > 0)
            timeParts.Add($"{abs.Hours}H");
        if (abs.Minutes > 0)
            timeParts.Add($"{abs.Minutes}M");
        if (abs.Seconds > 0 || abs.Milliseconds > 0)
        {
            var seconds = abs.Seconds + abs.Milliseconds / 1000.0;
            timeParts.Add(abs.Milliseconds > 0 ? $"{seconds:0.###}S" : $"{abs.Seconds}S");
        }

        if (timeParts.Count > 0 || parts.Count == 0)
            return $"{negative}P{string.Join("", parts)}T{string.Join("", timeParts)}";

        return $"{negative}P{string.Join("", parts)}";
    }

    /// <summary>Parses an ISO 8601 duration string.</summary>
    public static Duration Parse(string s)
    {
        if (TryParse(s, out var result))
            return result;
        throw new FormatException($"'{s}' is not a valid ISO 8601 duration.");
    }

    /// <inheritdoc />
    static Duration IParsable<Duration>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <inheritdoc />
    static bool IParsable<Duration>.TryParse(string? s, IFormatProvider? provider, out Duration result)
        => TryParse(s, out result);

    /// <summary>Tries to parse an ISO 8601 duration string.</summary>
    public static bool TryParse(string? s, out Duration result)
    {
        result = Zero;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var span = s.AsSpan().Trim();
        var negative = false;

        if (span[0] == '-')
        {
            negative = true;
            span = span[1..];
        }

        if (span.Length < 2 || span[0] != 'P')
            return false;
        span = span[1..];

        var totalTicks = 0L;
        var inTimePart = false;

        while (span.Length > 0)
        {
            if (span[0] == 'T')
            {
                inTimePart = true;
                span = span[1..];
                continue;
            }

            // Find the number
            var numEnd = 0;
            while (numEnd < span.Length && (char.IsDigit(span[numEnd]) || span[numEnd] == '.'))
                numEnd++;

            if (numEnd == 0 || numEnd >= span.Length)
                return false;

            if (!double.TryParse(span[..numEnd].ToString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value))
                return false;

            var unit = span[numEnd];
            span = span[(numEnd + 1)..];

            var ticks = unit switch
            {
                'D' when !inTimePart => (long)(value * TimeSpan.TicksPerDay),
                'H' when inTimePart => (long)(value * TimeSpan.TicksPerHour),
                'M' when inTimePart => (long)(value * TimeSpan.TicksPerMinute),
                'S' when inTimePart => (long)(value * TimeSpan.TicksPerSecond),
                _ => -1L
            };

            if (ticks < 0)
            {
                result = default;
                return false;
            }

            totalTicks += ticks;
        }

        result = new Duration(TimeSpan.FromTicks(negative ? -totalTicks : totalTicks));
        return true;
    }

    #endregion
}
