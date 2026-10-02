using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Internal;
using Pragmatic.Temporal.Timezone;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Context;

/// <summary>
///     Per-request temporal context. Holds timezone info and clock for the current request.
///     This is a boundary object - keep it simple.
/// </summary>
public class TemporalContext
{
    /// <summary>Client's timezone (from header, user profile, etc.).</summary>
    public required TimeZoneInfo ClientTimeZone { get; init; }

    /// <summary>Business timezone for calculations.</summary>
    public required TimeZoneInfo BusinessTimeZone { get; init; }

    /// <summary>The clock to use.</summary>
    public required IClock Clock { get; init; }

    /// <summary>
    ///     Policy applied when converting a local time that falls in a DST spring-forward gap
    ///     and no explicit policy is supplied. Default: <see cref="NonExistentTimePolicy.ShiftForward" />.
    /// </summary>
    public NonExistentTimePolicy NonExistentTimeHandling { get; init; } = NonExistentTimePolicy.ShiftForward;

    /// <summary>
    ///     Policy applied when converting a local time that is ambiguous (DST fall-back)
    ///     and no explicit policy is supplied. Default: <see cref="AmbiguousTimePolicy.UseStandardTime" />.
    /// </summary>
    public AmbiguousTimePolicy AmbiguousTimeHandling { get; init; } = AmbiguousTimePolicy.UseStandardTime;

    /// <summary>First day of the week for week calculations. Default: Monday (ISO 8601).</summary>
    public DayOfWeek FirstDayOfWeek { get; init; } = DayOfWeek.Monday;

    /// <summary>Default country code for holiday-aware operations. Default: null.</summary>
    public string? DefaultCountryCode { get; init; }

    #region Current Time

    /// <summary>Current UTC time.</summary>
    public DateTimeOffset UtcNow => Clock.UtcNow;

    /// <summary>Current time in client's timezone.</summary>
    public ZonedDateTime ClientNow => ZonedDateTime.FromUtc(UtcNow, ClientTimeZone);

    /// <summary>Current time in business timezone.</summary>
    public ZonedDateTime BusinessNow => ZonedDateTime.FromUtc(UtcNow, BusinessTimeZone);

    /// <summary>Today's date in client's timezone.</summary>
    public LocalDate ClientToday => new(ClientNow.LocalDate);

    /// <summary>Today's date in business timezone.</summary>
    public LocalDate BusinessToday => new(BusinessNow.LocalDate);

    /// <summary>Today's date in UTC.</summary>
    public LocalDate UtcToday => new(Clock.UtcToday);

    #endregion

    #region Conversions

    /// <summary>Converts UTC time to client's timezone.</summary>
    public ZonedDateTime ToClientTime(DateTimeOffset utc)
    {
        return ZonedDateTime.FromUtc(utc, ClientTimeZone);
    }

    /// <summary>Converts UTC time to business timezone.</summary>
    public ZonedDateTime ToBusinessTime(DateTimeOffset utc)
    {
        return ZonedDateTime.FromUtc(utc, BusinessTimeZone);
    }

    /// <summary>
    ///     Converts a client-local DateTime to UTC.
    ///     Uses <see cref="NonExistentTimePolicy.ShiftForward" /> and <see cref="AmbiguousTimePolicy.UseStandardTime" />.
    /// </summary>
    public DateTimeOffset ClientToUtc(DateTime clientLocal)
    {
        return LocalToUtc(clientLocal, ClientTimeZone,
            NonExistentTimeHandling, AmbiguousTimeHandling);
    }

    /// <summary>Converts a client-local DateTime to UTC with explicit DST policies.</summary>
    public DateTimeOffset ClientToUtc(
        DateTime clientLocal,
        NonExistentTimePolicy nonExistentPolicy,
        AmbiguousTimePolicy ambiguousPolicy)
    {
        return LocalToUtc(clientLocal, ClientTimeZone, nonExistentPolicy, ambiguousPolicy);
    }

    /// <summary>
    ///     Converts a business-local DateTime to UTC.
    ///     Uses <see cref="NonExistentTimePolicy.ShiftForward" /> and <see cref="AmbiguousTimePolicy.UseStandardTime" />.
    /// </summary>
    public DateTimeOffset BusinessToUtc(DateTime businessLocal)
    {
        return LocalToUtc(businessLocal, BusinessTimeZone,
            NonExistentTimeHandling, AmbiguousTimeHandling);
    }

    /// <summary>Converts a business-local DateTime to UTC with explicit DST policies.</summary>
    public DateTimeOffset BusinessToUtc(
        DateTime businessLocal,
        NonExistentTimePolicy nonExistentPolicy,
        AmbiguousTimePolicy ambiguousPolicy)
    {
        return LocalToUtc(businessLocal, BusinessTimeZone, nonExistentPolicy, ambiguousPolicy);
    }

    /// <summary>Converts a LocalDateTime in client timezone to UTC.</summary>
    public DateTimeOffset ClientToUtc(LocalDateTime localDateTime)
    {
        return ClientToUtc(localDateTime.ToDateTime());
    }

    /// <summary>Converts a LocalDateTime in client timezone to UTC with explicit DST policies.</summary>
    public DateTimeOffset ClientToUtc(
        LocalDateTime localDateTime,
        NonExistentTimePolicy nonExistentPolicy,
        AmbiguousTimePolicy ambiguousPolicy)
    {
        return ClientToUtc(localDateTime.ToDateTime(), nonExistentPolicy, ambiguousPolicy);
    }

    /// <summary>Converts a LocalDateTime in business timezone to UTC.</summary>
    public DateTimeOffset BusinessToUtc(LocalDateTime localDateTime)
    {
        return BusinessToUtc(localDateTime.ToDateTime());
    }

    /// <summary>Converts a LocalDateTime in business timezone to UTC with explicit DST policies.</summary>
    public DateTimeOffset BusinessToUtc(
        LocalDateTime localDateTime,
        NonExistentTimePolicy nonExistentPolicy,
        AmbiguousTimePolicy ambiguousPolicy)
    {
        return BusinessToUtc(localDateTime.ToDateTime(), nonExistentPolicy, ambiguousPolicy);
    }

    /// <summary>
    ///     Converts a local DateTime to UTC with explicit handling of DST edge cases.
    ///     Delegates to <see cref="DstHelper" /> for shared DST logic.
    /// </summary>
    private static DateTimeOffset LocalToUtc(
        DateTime localTime,
        TimeZoneInfo zone,
        NonExistentTimePolicy nonExistentPolicy,
        AmbiguousTimePolicy ambiguousPolicy)
    {
        return DstHelper.LocalToUtc(localTime, zone, nonExistentPolicy, ambiguousPolicy);
    }

    #endregion

    #region Date Range Helpers

    /// <summary>
    ///     Gets the UTC instant for start of day in client's timezone.
    ///     Useful for database queries.
    /// </summary>
    public DateTimeOffset ClientStartOfDay(LocalDate date)
    {
        return StartOfDay(date, ClientTimeZone);
    }

    /// <summary>
    ///     Gets the UTC instant for end of day (exclusive) in client's timezone.
    ///     Useful for database queries.
    /// </summary>
    public DateTimeOffset ClientEndOfDay(LocalDate date)
    {
        return EndOfDay(date, ClientTimeZone);
    }

    /// <summary>
    ///     Gets the UTC instant for start of day in business timezone.
    /// </summary>
    public DateTimeOffset BusinessStartOfDay(LocalDate date)
    {
        return StartOfDay(date, BusinessTimeZone);
    }

    /// <summary>
    ///     Gets the UTC instant for end of day (exclusive) in business timezone.
    /// </summary>
    public DateTimeOffset BusinessEndOfDay(LocalDate date)
    {
        return EndOfDay(date, BusinessTimeZone);
    }

    /// <summary>
    ///     Gets the UTC range for "today" in client timezone.
    /// </summary>
    public (DateTimeOffset Start, DateTimeOffset End) ClientTodayRange
    {
        get
        {
            var today = ClientToday;
            return (ClientStartOfDay(today), ClientEndOfDay(today));
        }
    }

    /// <summary>
    ///     Gets the UTC range for "today" in business timezone.
    /// </summary>
    public (DateTimeOffset Start, DateTimeOffset End) BusinessTodayRange
    {
        get
        {
            var today = BusinessToday;
            return (BusinessStartOfDay(today), BusinessEndOfDay(today));
        }
    }

    private DateTimeOffset StartOfDay(LocalDate date, TimeZoneInfo zone)
    {
        var localMidnight = date.ToDateTime();
        // Midnight is rarely in a DST gap, but handle it per the configured policies.
        return LocalToUtc(localMidnight, zone,
            NonExistentTimeHandling, AmbiguousTimeHandling);
    }

    private DateTimeOffset EndOfDay(LocalDate date, TimeZoneInfo zone)
    {
        return StartOfDay(date.AddDays(1), zone);
    }

    #endregion

    #region Factory Methods

    /// <summary>
    ///     Creates a TemporalContext with UTC for both client and business timezones.
    /// </summary>
    public static TemporalContext Utc(IClock? clock = null)
    {
        return new TemporalContext
        {
            Clock = clock ?? SystemClock.Instance,
            ClientTimeZone = TimeZoneInfo.Utc,
            BusinessTimeZone = TimeZoneInfo.Utc
        };
    }

    /// <summary>
    ///     Creates a TemporalContext with the specified timezone for both client and business.
    /// </summary>
    public static TemporalContext ForZone(TimeZoneInfo zone, IClock? clock = null)
    {
        return new TemporalContext
        {
            Clock = clock ?? SystemClock.Instance,
            ClientTimeZone = zone,
            BusinessTimeZone = zone
        };
    }

    /// <summary>
    ///     Creates a TemporalContext with the specified timezone for both client and business.
    /// </summary>
    public static TemporalContext ForZone(string timezoneId, IClock? clock = null)
    {
        var zone = TimeZoneResolver.GetTimeZone(timezoneId);
        return ForZone(zone, clock);
    }

    #endregion
}