namespace Pragmatic.Temporal.Types;

/// <summary>
///     Represents a single field within a cron expression (e.g., minutes, hours, day-of-month).
///     Supports wildcards, ranges, lists, steps, L (last day), W (weekday), and # (nth weekday).
/// </summary>
internal readonly struct CronField
{
    private readonly HashSet<int>? _values;
    private readonly bool _hasL; // Last day (L)
    private readonly bool _hasW; // Nearest weekday (W)
    private readonly int _nthDayOfWeek; // For # (e.g., 0#2 = second Sunday)

    public static CronField Zero { get; } = new(new HashSet<int> { 0 }, false, false, false, 0, false);

    private CronField(HashSet<int>? values, bool isWildcard, bool hasL, bool hasW, int nthDayOfWeek,
        bool isStarBased)
    {
        _values = values;
        IsWildcard = isWildcard;
        _hasL = hasL;
        _hasW = hasW;
        _nthDayOfWeek = nthDayOfWeek;
        IsStarBased = isStarBased;
    }

    /// <summary>Whether this field matches any value (*).</summary>
    public bool IsWildcard { get; }

    /// <summary>
    ///     Whether the source token was star-based (*, ? or */n). Vixie cron treats
    ///     star-based minute/hour fields as "interval" schedules for DST purposes:
    ///     they keep firing as real time advances, so a repeated fall-back hour runs
    ///     in both offsets. A list or range (e.g. 0,30 or 2-4) is NOT star-based.
    /// </summary>
    public bool IsStarBased { get; }

    public bool Contains(int value)
    {
        return IsWildcard || (_values?.Contains(value) ?? false);
    }

    public bool Matches(int dayValue, DateTime date)
    {
        if (IsWildcard)
            return true;

        // Handle L (last day of month)
        if (_hasL)
        {
            var lastDay = DateTime.DaysInMonth(date.Year, date.Month);
            return dayValue == lastDay;
        }

        // Handle W (nearest weekday)
        if (_hasW && _values?.Count == 1)
        {
            var targetDay = _values.First();
            var targetDate = new DateTime(date.Year, date.Month,
                Math.Min(targetDay, DateTime.DaysInMonth(date.Year, date.Month)));

            if (targetDate.DayOfWeek == DayOfWeek.Saturday)
                targetDate = targetDate.AddDays(targetDay == 1 ? 2 : -1);
            else if (targetDate.DayOfWeek == DayOfWeek.Sunday)
                targetDate = targetDate.AddDays(targetDay == DateTime.DaysInMonth(date.Year, date.Month) ? -2 : 1);

            return date.Day == targetDate.Day;
        }

        return _values?.Contains(dayValue) ?? false;
    }

    public bool MatchesDayOfWeek(int dayOfWeekValue, DateTime date)
    {
        if (IsWildcard)
            return true;

        // Handle # (nth weekday of month)
        if (_nthDayOfWeek > 0 && _values?.Count == 1)
        {
            var targetDow = _values.First();
            if (dayOfWeekValue != targetDow)
                return false;

            var firstOfMonth = new DateTime(date.Year, date.Month, 1);
            var firstTarget = firstOfMonth.AddDays((7 + targetDow - (int)firstOfMonth.DayOfWeek) % 7);
            var nthTarget = firstTarget.AddDays((_nthDayOfWeek - 1) * 7);

            return date.Day == nthTarget.Day && nthTarget.Month == date.Month;
        }

        // Handle 7 as Sunday (some cron implementations use 7)
        if (dayOfWeekValue == 0 && (_values?.Contains(7) ?? false))
            return true;

        return _values?.Contains(dayOfWeekValue) ?? false;
    }

    public static CronField Parse(string field, int min, int max, CronFieldType fieldType)
    {
        if (field == "*" || field == "?")
            return new CronField(null, true, false, false, 0, true);

        // Handle L (last)
        if (field.Equals("L", StringComparison.OrdinalIgnoreCase) && fieldType == CronFieldType.DayOfMonth)
            return new CronField(null, false, true, false, 0, false);

        // Handle W (weekday)
        if (field.EndsWith("W", StringComparison.OrdinalIgnoreCase) && fieldType == CronFieldType.DayOfMonth)
        {
            var dayStr = field[..^1];
            if (int.TryParse(dayStr, out var day) && day is >= 1 and <= 31)
                return new CronField(new HashSet<int> { day }, false, false, true, 0, false);
            throw new FormatException($"Invalid W modifier: {field}");
        }

        // Handle # (nth weekday)
        if (field.Contains('#') && fieldType == CronFieldType.DayOfWeek)
        {
            var parts = field.Split('#');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var dow) && dow is >= 0 and <= 7 &&
                int.TryParse(parts[1], out var nth) && nth is >= 1 and <= 5)
                return new CronField(new HashSet<int> { dow == 7 ? 0 : dow }, false, false, false, nth, false);
            throw new FormatException($"Invalid # modifier: {field}");
        }

        var values = new HashSet<int>();

        // Day-of-week accepts 7 as an alias for Sunday (0); allow it past the [0,6] range.
        var upperBound = fieldType == CronFieldType.DayOfWeek ? Math.Max(max, 7) : max;

        foreach (var part in field.Split(','))
            if (part.Contains('/'))
            {
                var stepParts = part.Split('/');
                if (stepParts.Length != 2 || !int.TryParse(stepParts[1], out var step) || step <= 0)
                    throw new FormatException($"Invalid step: {part}");

                int start, end;
                if (stepParts[0] == "*")
                {
                    start = min;
                    end = max;
                }
                else if (stepParts[0].Contains('-'))
                {
                    var rangeParts = stepParts[0].Split('-');
                    start = ParseValue(rangeParts[0], fieldType, min, upperBound);
                    end = ParseValue(rangeParts[1], fieldType, min, upperBound);
                }
                else
                {
                    start = ParseValue(stepParts[0], fieldType, min, upperBound);
                    end = max;
                }

                for (var i = start; i <= end; i += step)
                    values.Add(i);
            }
            else if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                var start = ParseValue(rangeParts[0], fieldType, min, upperBound);
                var end = ParseValue(rangeParts[1], fieldType, min, upperBound);

                if (start > end)
                    throw new FormatException($"Invalid range: {part}");

                for (var i = start; i <= end; i++)
                    values.Add(i);
            }
            else
            {
                values.Add(ParseValue(part, fieldType, min, upperBound));
            }

        // A step over the full range (*/n) is star-based like * itself; explicit
        // values, lists and ranges are not.
        var isStarBased = field.StartsWith("*/", StringComparison.Ordinal);
        return new CronField(values, false, false, false, 0, isStarBased);
    }

    private static int ParseValue(string value, CronFieldType fieldType, int min, int max)
    {
        // Handle month names
        if (fieldType == CronFieldType.Month)
            value = value.ToUpperInvariant() switch
            {
                "JAN" => "1",
                "FEB" => "2",
                "MAR" => "3",
                "APR" => "4",
                "MAY" => "5",
                "JUN" => "6",
                "JUL" => "7",
                "AUG" => "8",
                "SEP" => "9",
                "OCT" => "10",
                "NOV" => "11",
                "DEC" => "12",
                _ => value
            };

        // Handle day of week names
        if (fieldType == CronFieldType.DayOfWeek)
            value = value.ToUpperInvariant() switch
            {
                "SUN" => "0",
                "MON" => "1",
                "TUE" => "2",
                "WED" => "3",
                "THU" => "4",
                "FRI" => "5",
                "SAT" => "6",
                _ => value
            };

        if (int.TryParse(value, out var result))
        {
            if (result < min || result > max)
                throw new FormatException($"Value {result} is out of range [{min},{max}] for {fieldType}.");
            return result;
        }

        throw new FormatException($"Invalid value: {value}");
    }
}
