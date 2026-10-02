using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Internal;
using Pragmatic.Temporal.Timezone;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Temporal.Types;

/// <summary>
///     A date/time value with explicit timezone information.
///     Use this when you need to preserve the original timezone context,
///     such as for scheduling, display, or when the timezone is semantically meaningful.
///     <para>
///         ⚠️ Constructors are restricted. Use factory methods to ensure DST safety.
///     </para>
/// </summary>
public readonly partial struct ZonedDateTime : IEquatable<ZonedDateTime>, IComparable<ZonedDateTime>, IComparable
{
    private readonly DateTimeOffset _utcInstant;

    internal ZonedDateTime(DateTimeOffset utcInstant, TimeZoneInfo zone)
    {
        ThrowIfNull(zone);
        _utcInstant = utcInstant.ToUniversalTime();
        Zone = zone;
    }

    #region Properties

    /// <summary>Gets the underlying UTC instant.</summary>
    public DateTimeOffset UtcDateTime => _utcInstant;

    /// <summary>Gets the timezone this value represents.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>Gets the IANA timezone ID.</summary>
    public string ZoneId => TimeZoneResolver.GetIanaId(Zone);

    /// <summary>Gets the local date/time in the zone.</summary>
    public DateTime LocalDateTime => TimeZoneInfo.ConvertTimeFromUtc(_utcInstant.UtcDateTime, Zone);

    /// <summary>Gets the local date in the zone.</summary>
    public DateOnly LocalDate => DateOnly.FromDateTime(LocalDateTime);

    /// <summary>Gets the local time of day in the zone.</summary>
    public TimeOnly LocalTime => TimeOnly.FromDateTime(LocalDateTime);

    /// <summary>Gets the local date as a <see cref="LocalDate" />.</summary>
    public LocalDate Date => new(LocalDate);

    /// <summary>Gets the local time as a <see cref="LocalTime" />.</summary>
    public LocalTime Time => new(LocalTime);

    /// <summary>Gets the offset from UTC at this instant in this zone.</summary>
    public TimeSpan Offset => Zone.GetUtcOffset(_utcInstant.UtcDateTime);

    /// <summary>Gets the year component in the local timezone.</summary>
    public int Year => LocalDateTime.Year;

    /// <summary>Gets the month component (1-12) in the local timezone.</summary>
    public int Month => LocalDateTime.Month;

    /// <summary>Gets the day component (1-31) in the local timezone.</summary>
    public int Day => LocalDateTime.Day;

    /// <summary>Gets the hour component (0-23) in the local timezone.</summary>
    public int Hour => LocalDateTime.Hour;

    /// <summary>Gets the minute component (0-59) in the local timezone.</summary>
    public int Minute => LocalDateTime.Minute;

    /// <summary>Gets the second component (0-59) in the local timezone.</summary>
    public int Second => LocalDateTime.Second;

    /// <summary>Gets the day of week in the local timezone.</summary>
    public DayOfWeek DayOfWeek => LocalDateTime.DayOfWeek;

    /// <summary>Returns true if daylight saving time is in effect at this instant in this zone.</summary>
    public bool IsDaylightSavingTime => Zone.IsDaylightSavingTime(_utcInstant.UtcDateTime);

    #endregion

    #region Factory Methods (Safe Entry Points)

    /// <summary>
    ///     Creates a ZonedDateTime from a UTC instant.
    ///     This is the safest factory method as UTC has no DST ambiguity.
    /// </summary>
    public static ZonedDateTime FromUtc(DateTimeOffset utc, TimeZoneInfo zone)
    {
        ThrowIfNull(zone);
        return new ZonedDateTime(utc.ToUniversalTime(), zone);
    }

    /// <summary>
    ///     Creates a ZonedDateTime from a UTC instant.
    /// </summary>
    public static ZonedDateTime FromUtc(DateTimeOffset utc, string timezoneId)
    {
        var zone = TimeZoneResolver.GetTimeZone(timezoneId);
        return FromUtc(utc, zone);
    }

    /// <summary>
    ///     Creates a ZonedDateTime from a local DateTime with explicit DST policies.
    ///     Delegates to <see cref="DstHelper" /> for DST-safe conversion.
    /// </summary>
    /// <param name="localDateTime">The local date and time (Kind is ignored).</param>
    /// <param name="zone">The timezone to interpret the local time in.</param>
    /// <param name="nonExistentPolicy">How to handle times that don't exist (DST spring forward).</param>
    /// <param name="ambiguousPolicy">How to handle ambiguous times (DST fall back).</param>
    public static ZonedDateTime FromLocal(
        DateTime localDateTime,
        TimeZoneInfo zone,
        NonExistentTimePolicy nonExistentPolicy = NonExistentTimePolicy.ShiftForward,
        AmbiguousTimePolicy ambiguousPolicy = AmbiguousTimePolicy.UseStandardTime)
    {
        ThrowIfNull(zone);
        var utc = DstHelper.LocalToUtc(localDateTime, zone, nonExistentPolicy, ambiguousPolicy);
        return new ZonedDateTime(utc, zone);
    }

    /// <summary>
    ///     Creates a ZonedDateTime from a local DateTime.
    ///     Throws if the time is ambiguous or non-existent.
    /// </summary>
    public static ZonedDateTime FromLocalStrict(DateTime localDateTime, TimeZoneInfo zone)
    {
        return FromLocal(localDateTime, zone,
            NonExistentTimePolicy.ThrowException,
            AmbiguousTimePolicy.ThrowException);
    }

    /// <summary>
    ///     Gets the current time in the specified timezone.
    /// </summary>
    public static ZonedDateTime Now(TimeZoneInfo zone, IClock clock)
    {
        ThrowIfNull(zone);
        ThrowIfNull(clock);
        return FromUtc(clock.UtcNow, zone);
    }

    /// <summary>
    ///     Gets the current time in the specified timezone.
    /// </summary>
    public static ZonedDateTime Now(string timezoneId, IClock clock)
    {
        var zone = TimeZoneResolver.GetTimeZone(timezoneId);
        return Now(zone, clock);
    }

    #endregion

    #region Deconstruct

    /// <summary>Deconstructs into date, time, and zone components.</summary>
    public void Deconstruct(out DateOnly date, out TimeOnly time, out TimeZoneInfo zone)
    {
        date = LocalDate;
        time = LocalTime;
        zone = Zone;
    }

    /// <summary>Deconstructs into UTC instant and zone.</summary>
    public void Deconstruct(out DateTimeOffset utc, out TimeZoneInfo zone)
    {
        utc = _utcInstant;
        zone = Zone;
    }

    #endregion

    #region Operators

    public static bool operator ==(ZonedDateTime left, ZonedDateTime right)
    {
        return left._utcInstant == right._utcInstant;
    }

    public static bool operator !=(ZonedDateTime left, ZonedDateTime right)
    {
        return left._utcInstant != right._utcInstant;
    }

    public static bool operator <(ZonedDateTime left, ZonedDateTime right)
    {
        return left._utcInstant < right._utcInstant;
    }

    public static bool operator <=(ZonedDateTime left, ZonedDateTime right)
    {
        return left._utcInstant <= right._utcInstant;
    }

    public static bool operator >(ZonedDateTime left, ZonedDateTime right)
    {
        return left._utcInstant > right._utcInstant;
    }

    public static bool operator >=(ZonedDateTime left, ZonedDateTime right)
    {
        return left._utcInstant >= right._utcInstant;
    }

    public static ZonedDateTime operator +(ZonedDateTime zdt, Duration duration)
    {
        return zdt.Add(duration);
    }

    public static ZonedDateTime operator -(ZonedDateTime zdt, Duration duration)
    {
        return zdt.Add(-duration);
    }

    public static Duration operator -(ZonedDateTime left, ZonedDateTime right)
    {
        return right.DurationUntil(left);
    }

    #endregion

    #region Equality & Comparison

    /// <summary>
    ///     Checks equality based on the UTC instant.
    ///     Two ZonedDateTimes are equal if they represent the same instant in time,
    ///     regardless of timezone.
    /// </summary>
    public bool Equals(ZonedDateTime other)
    {
        return _utcInstant == other._utcInstant;
    }

    /// <summary>
    ///     Checks equality including timezone.
    ///     Returns true only if both the instant AND the timezone are the same.
    /// </summary>
    public bool EqualsExact(ZonedDateTime other)
    {
        return _utcInstant == other._utcInstant && Zone.Id == other.Zone.Id;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is ZonedDateTime other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _utcInstant.GetHashCode();
    }

    public int CompareTo(ZonedDateTime other)
    {
        return _utcInstant.CompareTo(other._utcInstant);
    }

    public int CompareTo(object? obj)
    {
        return obj is ZonedDateTime other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(ZonedDateTime)}", nameof(obj));
    }

    #endregion
}