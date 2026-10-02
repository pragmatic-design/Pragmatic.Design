using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Temporal.Types;

/// <summary>
///     Represents a closed date range [Start, End] with both endpoints included.
///     Useful for date-based queries, reporting periods, and overlap detection.
/// </summary>
/// <remarks>
///     This is an inclusive range: both Start and End dates are part of the range.
///     For an empty range, use <see cref="Empty" />.
/// </remarks>
public readonly partial struct DateRange : IEquatable<DateRange>, IEnumerable<LocalDate>
{
    /// <summary>
    ///     Gets the start date of the range (inclusive).
    /// </summary>
    public LocalDate Start { get; }

    /// <summary>
    ///     Gets the end date of the range (inclusive).
    /// </summary>
    public LocalDate End { get; }

    /// <summary>
    ///     Creates a new date range from start to end (inclusive).
    /// </summary>
    /// <param name="start">The start date (inclusive).</param>
    /// <param name="end">The end date (inclusive).</param>
    /// <exception cref="ArgumentException">Thrown when start is after end.</exception>
    public DateRange(LocalDate start, LocalDate end)
    {
        if (start > end)
            throw new ArgumentException($"Start date ({start}) cannot be after end date ({end}).", nameof(start));

        Start = start;
        End = end;
    }

    #region Properties

    /// <summary>
    ///     Gets the number of days in the range (inclusive of both endpoints).
    ///     Zero for <see cref="Empty" />.
    /// </summary>
    public int Days => IsEmpty ? 0 : End.DaysBetween(Start) + 1;

    /// <summary>
    ///     Gets whether this range represents a single day.
    ///     False for <see cref="Empty" />.
    /// </summary>
    public bool IsSingleDay => !IsEmpty && Start == End;

    /// <summary>
    ///     Gets whether this range is empty. An empty range contains no dates,
    ///     enumerates nothing, and overlaps with nothing.
    /// </summary>
    public bool IsEmpty => this == Empty;

    /// <summary>
    ///     Gets the duration of the range as a <see cref="Duration" />.
    /// </summary>
    public Duration Duration => Duration.FromDays(Days);

    #endregion

    #region Operations

    /// <summary>
    ///     Checks if the specified date is within this range (inclusive).
    ///     Always false for <see cref="Empty" />.
    /// </summary>
    public bool Contains(LocalDate date) => !IsEmpty && date >= Start && date <= End;

    /// <summary>
    ///     Checks if the specified range is entirely within this range.
    ///     Always false when either range is <see cref="Empty" />.
    /// </summary>
    public bool Contains(DateRange other)
        => !IsEmpty && !other.IsEmpty && other.Start >= Start && other.End <= End;

    /// <summary>
    ///     Checks if this range overlaps with another range.
    ///     Always false when either range is <see cref="Empty" />.
    /// </summary>
    public bool Overlaps(DateRange other)
        => !IsEmpty && !other.IsEmpty && Start <= other.End && End >= other.Start;

    /// <summary>
    ///     Checks if this range is adjacent to another range (no gap, no overlap).
    ///     Always false when either range is <see cref="Empty" />.
    /// </summary>
    public bool IsAdjacentTo(DateRange other)
    {
        if (IsEmpty || other.IsEmpty)
            return false;

        return End.AddDays(1) == other.Start || other.End.AddDays(1) == Start;
    }

    /// <summary>
    ///     Returns the intersection of this range with another, or Empty if no overlap.
    /// </summary>
    public DateRange Intersect(DateRange other)
    {
        if (!Overlaps(other))
            return Empty;

        var start = Start > other.Start ? Start : other.Start;
        var end = End < other.End ? End : other.End;
        return new DateRange(start, end);
    }

    /// <summary>
    ///     Returns the smallest range that contains both this range and another.
    /// </summary>
    public DateRange Union(DateRange other)
    {
        if (IsEmpty)
            return other;
        if (other.IsEmpty)
            return this;

        var start = Start < other.Start ? Start : other.Start;
        var end = End > other.End ? End : other.End;
        return new DateRange(start, end);
    }

    /// <summary>
    ///     Extends the range by the specified number of days on both ends.
    /// </summary>
    public DateRange Expand(int days)
    {
        return new DateRange(Start.AddDays(-days), End.AddDays(days));
    }

    /// <summary>
    ///     Shifts the entire range by the specified number of days.
    /// </summary>
    public DateRange Shift(int days)
    {
        return new DateRange(Start.AddDays(days), End.AddDays(days));
    }

    /// <summary>
    ///     Splits this range into multiple ranges of the specified size.
    /// </summary>
    /// <param name="daysPerChunk">Maximum days per chunk.</param>
    public IEnumerable<DateRange> Split(int daysPerChunk)
    {
        if (daysPerChunk < 1)
            throw new ArgumentOutOfRangeException(nameof(daysPerChunk), "Days per chunk must be at least 1.");

        var current = Start;
        while (current <= End)
        {
            var chunkEnd = current.AddDays(daysPerChunk - 1);
            if (chunkEnd > End)
                chunkEnd = End;

            yield return new DateRange(current, chunkEnd);
            current = chunkEnd.AddDays(1);
        }
    }

    #endregion

    #region Enumeration

    /// <summary>
    ///     Enumerates all dates in the range. Yields nothing for <see cref="Empty" />.
    /// </summary>
    public IEnumerator<LocalDate> GetEnumerator()
    {
        if (IsEmpty)
            yield break;

        var current = Start;
        while (current <= End)
        {
            yield return current;
            current = current.AddDays(1);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    ///     Returns all dates in the range as a list.
    /// </summary>
    public List<LocalDate> ToList()
    {
        var list = new List<LocalDate>(Days);
        foreach (var date in this)
            list.Add(date);
        return list;
    }

    /// <summary>
    ///     Returns only weekdays in the range.
    /// </summary>
    public IEnumerable<LocalDate> Weekdays()
    {
        foreach (var date in this)
        {
            if (date.IsWeekday)
                yield return date;
        }
    }

    /// <summary>
    ///     Returns only weekends in the range.
    /// </summary>
    public IEnumerable<LocalDate> Weekends()
    {
        foreach (var date in this)
        {
            if (date.IsWeekend)
                yield return date;
        }
    }

    #endregion

    #region Equality & Comparison

    public bool Equals(DateRange other) => Start == other.Start && End == other.End;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is DateRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Start, End);

    public static bool operator ==(DateRange left, DateRange right) => left.Equals(right);

    public static bool operator !=(DateRange left, DateRange right) => !left.Equals(right);

    #endregion

    #region Formatting

    /// <summary>
    ///     Returns the range in ISO 8601 interval format: "start/end".
    /// </summary>
    public override string ToString() => $"{Start}/{End}";

    /// <summary>
    ///     Parses a date range from ISO 8601 interval format: "start/end".
    /// </summary>
    public static DateRange Parse(string s)
    {
        if (TryParse(s, out var result))
            return result;
        throw new FormatException($"'{s}' is not a valid date range format. Expected: 'yyyy-MM-dd/yyyy-MM-dd'.");
    }

    /// <summary>
    ///     Tries to parse a date range from ISO 8601 interval format.
    /// </summary>
    public static bool TryParse(string? s, out DateRange result)
    {
        result = Empty;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var parts = s.Split('/');
        if (parts.Length != 2)
            return false;

        if (!LocalDate.TryParse(parts[0], out var start))
            return false;
        if (!LocalDate.TryParse(parts[1], out var end))
            return false;

        if (start > end)
            return false;

        result = new DateRange(start, end);
        return true;
    }

    #endregion

    /// <summary>
    ///     Deconstructs the range into start and end dates.
    /// </summary>
    public void Deconstruct(out LocalDate start, out LocalDate end)
    {
        start = Start;
        end = End;
    }
}
